using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class ClickHouseImpressionLogTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(Status) { Content = new StringContent("") };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:8123/") };
    }

    private static (ClickHouseImpressionLog Log, RecordingHandler Handler) Build(int batchSize = 200)
    {
        var handler = new RecordingHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClickHouse:Database"] = "explained",
                ["ClickHouse:ImpressionsTable"] = "feed_impressions",
                ["ClickHouse:BatchSize"] = batchSize.ToString()
            })
            .Build();

        var log = new ClickHouseImpressionLog(
            new SingleClientFactory(handler), configuration, NullLogger<ClickHouseImpressionLog>.Instance);

        return (log, handler);
    }

    private static FeedImpression Impression(
        string feedId = "f1", FeedLevel level = FeedLevel.Personalized) =>
        new(feedId, "u1", ["a", "b"], level, DateTimeOffset.UtcNow);

    [Fact]
    public async Task WritesJsonEachRowIntoTheConfiguredTable()
    {
        var (log, handler) = Build();

        await log.LogAsync(Impression(), CancellationToken.None);
        await log.FlushAsync(CancellationToken.None);

        var body = Assert.Single(handler.Bodies);

        Assert.StartsWith("INSERT INTO explained.feed_impressions FORMAT JSONEachRow", body);
        Assert.Contains("\"feed_id\":\"f1\"", body);
        Assert.Contains("\"article_ids\":[\"a\",\"b\"]", body);
    }

    [Fact]
    public async Task WritesTheLevelInItsWireForm()
    {
        var (log, handler) = Build();

        await log.LogAsync(Impression(level: FeedLevel.Unranked), CancellationToken.None);
        await log.FlushAsync(CancellationToken.None);

        Assert.Contains("\"level\":\"unranked\"", handler.Bodies[0]);
    }

    [Fact]
    public async Task LoggingDoesNotBlockOnTheNetwork()
    {
        var (log, handler) = Build();

        await log.LogAsync(Impression(), CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task NothingBufferedMeansNoRequest()
    {
        var (log, handler) = Build();

        await log.FlushAsync(CancellationToken.None);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task ABatchIsSentAsOneInsert()
    {
        var (log, handler) = Build();

        for (var i = 0; i < 5; i++)
        {
            await log.LogAsync(Impression($"f{i}"), CancellationToken.None);
        }

        await log.FlushAsync(CancellationToken.None);

        Assert.Single(handler.Bodies);
        Assert.Equal(5, handler.Bodies[0].Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
    }

    [Fact]
    public async Task FlushesAtMostOneBatchAtATime()
    {
        var (log, handler) = Build(batchSize: 2);

        for (var i = 0; i < 5; i++)
        {
            await log.LogAsync(Impression($"f{i}"), CancellationToken.None);
        }

        await log.FlushAsync(CancellationToken.None);

        Assert.Equal(2, handler.Bodies[0].Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
    }

    [Fact]
    public async Task AClickHouseErrorDoesNotThrow()
    {
        var (log, handler) = Build();
        handler.Status = HttpStatusCode.InternalServerError;

        await log.LogAsync(Impression(), CancellationToken.None);

        await log.FlushAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TheBufferIsBoundedSoAnOutageCostsMemoryNotTheProcess()
    {
        var (log, handler) = Build();
        var bounded = new ClickHouseImpressionLog(
            new SingleClientFactory(handler),
            new ConfigurationBuilder().Build(),
            NullLogger<ClickHouseImpressionLog>.Instance)
        { MaxBuffered = 2 };

        for (var i = 0; i < 10; i++)
        {
            await bounded.LogAsync(Impression($"f{i}"), CancellationToken.None);
        }

        await bounded.FlushAsync(CancellationToken.None);

        Assert.Equal(2, handler.Bodies[0].Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        Assert.NotNull(log);
    }

    [Fact]
    public async Task StoppingFlushesWhatIsLeft()
    {
        var (log, handler) = Build();

        await log.StartAsync(CancellationToken.None);
        await log.LogAsync(Impression(), CancellationToken.None);
        await log.StopAsync(CancellationToken.None);

        Assert.NotEmpty(handler.Bodies);
    }
}
