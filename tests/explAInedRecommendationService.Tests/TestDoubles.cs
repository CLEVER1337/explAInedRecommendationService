using Microsoft.Extensions.Options;

public sealed class FakeArticleClient : IArticleClient
{
    private readonly Dictionary<string, ArticleSummary> _articles = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    public bool Faulted { get; set; }

    public TimeSpan ArtificialDelay { get; set; } = TimeSpan.Zero;

    public ArticleSummary Seed(string id, DateTime? publishedAt = null)
    {
        var article = new ArticleSummary(id, $"title-{id}", $"desc-{id}", "tag", "author", publishedAt ?? DateTime.UtcNow, 10);

        if (!_articles.ContainsKey(id)) _order.Add(id);
        _articles[id] = article;

        return article;
    }

    public void SeedMany(params string[] ids)
    {
        foreach (var id in ids) Seed(id);
    }

    public void Clear()
    {
        _articles.Clear();
        _order.Clear();
        Faulted = false;
        ArtificialDelay = TimeSpan.Zero;
    }

    public async Task<IReadOnlyList<ArticleSummary>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        await GateAsync(ct);

        return ids.Where(_articles.ContainsKey).Select(id => _articles[id]).ToList();
    }

    public async Task<IReadOnlyList<ArticleSummary>> GetRecentAsync(int limit, int offset, CancellationToken ct)
    {
        await GateAsync(ct);

        return _order.Skip(offset).Take(limit).Select(id => _articles[id]).ToList();
    }

    private async Task GateAsync(CancellationToken ct)
    {
        if (ArtificialDelay > TimeSpan.Zero) await Task.Delay(ArtificialDelay, ct);
        if (Faulted) throw new HttpRequestException("fake article client faulted");
    }
}

public sealed class FakeFaissClient : IFaissClient
{
    private readonly Dictionary<string, IReadOnlyList<string>> _hits = new(StringComparer.Ordinal);

    public bool Faulted { get; set; }

    public TimeSpan ArtificialDelay { get; set; } = TimeSpan.Zero;

    public int? LastTopK { get; private set; }

    /// <summary>Users without a seed have no embedding — the client returns null, as 204 does.</summary>
    public void Seed(string userId, params string[] ids) => _hits[userId] = ids;

    public void Clear()
    {
        _hits.Clear();
        Faulted = false;
        ArtificialDelay = TimeSpan.Zero;
        LastTopK = null;
    }

    public async Task<FaissSearchResult?> SearchAsync(string userId, int topK, CancellationToken ct)
    {
        LastTopK = topK;

        if (ArtificialDelay > TimeSpan.Zero) await Task.Delay(ArtificialDelay, ct);
        if (Faulted) throw new HttpRequestException("fake faiss client faulted");

        if (!_hits.TryGetValue(userId, out var ids)) return null;

        var ranked = ids.Take(topK).ToList();
        var scores = ranked.Select((_, index) => 1d - index * 0.01).ToList();

        return new FaissSearchResult(ranked, scores);
    }
}

public sealed class FakeCandidateSource : ICandidateSource
{
    private readonly IReadOnlyList<string> _ids;

    public FakeCandidateSource(string name, CandidateSourceKind kind, params string[] ids)
    {
        Name = name;
        Kind = kind;
        _ids = ids;
    }

    public string Name { get; }

    public CandidateSourceKind Kind { get; }

    public bool Faulted { get; set; }

    public TimeSpan ArtificialDelay { get; set; } = TimeSpan.Zero;

    public async Task<CandidateBatch> FetchAsync(FeedRequest request, CancellationToken ct)
    {
        if (ArtificialDelay > TimeSpan.Zero) await Task.Delay(ArtificialDelay, ct);
        if (Faulted) throw new InvalidOperationException($"{Name} faulted");

        var items = _ids
            .Select((id, index) => new Candidate(id, _ids.Count - index, Name, index))
            .ToList();

        return CandidateBatch.Ok(Name, Kind, items);
    }
}

public sealed class StubRanker : IRanker
{
    public string Name => "stub-ranker";

    public bool Succeeds { get; set; } = true;

    public Task<RankResult> RankAsync(string userId, IReadOnlyList<Candidate> candidates, CancellationToken ct)
    {
        var ranked = candidates.Reverse().ToList();

        return Task.FromResult(new RankResult(ranked, Succeeds, Succeeds ? null : "stub failure"));
    }
}

public sealed class TestSnapshotStore : IFeedSnapshotStore
{
    private FeedSnapshot? _snapshot;

    public void Clear() => _snapshot = null;

    public FeedSnapshot? Get() => _snapshot;

    public void Update(IReadOnlyList<ArticleSummary> articles, FeedLevel producedAt)
    {
        if (articles.Count == 0) return;

        _snapshot = new FeedSnapshot(articles.ToList(), producedAt, DateTimeOffset.UtcNow);
    }
}

public static class TestFactories
{
    public static IOptions<FeedOptions> Options(Action<FeedOptions>? configure = null)
    {
        var options = new FeedOptions();
        configure?.Invoke(options);

        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static SourceExecutor Executor() =>
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<SourceExecutor>.Instance);
}
