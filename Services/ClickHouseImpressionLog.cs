using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

public sealed class ClickHouseImpressionLog : IImpressionLog, IHostedService, IAsyncDisposable
{
    public const string HttpClientName = "clickhouse-impressions";

    private readonly ConcurrentQueue<FeedImpression> _buffer = new();
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<ClickHouseImpressionLog> _logger;
    private readonly string _database;
    private readonly string _table;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;

    private CancellationTokenSource? _stopping;
    private Task? _pump;

    // This instance is registered three times — as itself, as IImpressionLog, and as a
    // hosted service — and the last two go through factory delegates. The container tracks
    // what a factory returns for disposal on each of those, so it ends up holding the same
    // object more than once and calls DisposeAsync more than once on shutdown.
    //
    // Disposing twice is meant to be safe, so the fix belongs here rather than in the
    // registrations: without this flag the second call reaches _stopping.Cancel() after
    // _stopping.Dispose() and throws ObjectDisposedException out of shutdown.
    private bool _disposed;

    public ClickHouseImpressionLog(
        IHttpClientFactory clients, IConfiguration configuration, ILogger<ClickHouseImpressionLog> logger)
    {
        _clients = clients;
        _logger = logger;
        _database = configuration["ClickHouse:Database"] ?? "explained";
        _table = configuration["ClickHouse:ImpressionsTable"] ?? "feed_impressions";
        _batchSize = configuration.GetValue("ClickHouse:BatchSize", 200);
        _flushInterval = TimeSpan.FromMilliseconds(configuration.GetValue("ClickHouse:FlushMs", 2000));
    }

    public int MaxBuffered { get; init; } = 10_000;

    public Task LogAsync(FeedImpression impression, CancellationToken ct)
    {
        if (_buffer.Count >= MaxBuffered)
        {
            _logger.LogWarning("impression buffer is full ({Max}); dropping this page", MaxBuffered);
            return Task.CompletedTask;
        }

        _buffer.Enqueue(impression);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        _pump = PumpAsync(_stopping.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping?.Cancel();

        if (_pump is not null)
        {
            try { await _pump; } catch (OperationCanceledException) { }
        }

        await FlushAsync(CancellationToken.None);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_flushInterval, ct);
                await FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "impression flush failed; rows for this interval are lost");
            }
        }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        var batch = new List<FeedImpression>(_batchSize);

        while (batch.Count < _batchSize && _buffer.TryDequeue(out var impression))
        {
            batch.Add(impression);
        }

        if (batch.Count == 0) return;

        var body = new StringBuilder();
        body.Append("INSERT INTO ").Append(_database).Append('.').Append(_table)
            .Append(" FORMAT JSONEachRow\n");

        foreach (var impression in batch)
        {
            body.Append(JsonSerializer.Serialize(new
            {
                feed_id = impression.FeedId,
                user_id = impression.UserId,
                article_ids = impression.ArticleIds,
                level = impression.Level.ToWire(),
                served_at = impression.ServedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff")
            })).Append('\n');
        }

        using var client = _clients.CreateClient(HttpClientName);
        using var content = new StringContent(body.ToString(), Encoding.UTF8);
        using var response = await client.PostAsync($"?database={_database}", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "ClickHouse rejected {Count} impressions ({Status}): {Detail}",
                batch.Count,
                (int)response.StatusCode,
                detail.Length > 300 ? detail[..300] : detail);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _stopping?.Cancel();
        _stopping?.Dispose();

        if (_pump is not null)
        {
            try { await _pump; } catch (OperationCanceledException) { }
        }
    }
}
