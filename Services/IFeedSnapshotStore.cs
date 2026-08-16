public interface IFeedSnapshotStore
{
    FeedSnapshot? Get();

    void Update(IReadOnlyList<ArticleSummary> articles, FeedLevel producedAt);
}

public sealed class InMemoryFeedSnapshotStore : IFeedSnapshotStore
{
    private readonly TimeSpan _maxAge;
    private volatile FeedSnapshot? _snapshot;

    public InMemoryFeedSnapshotStore(Microsoft.Extensions.Options.IOptions<FeedOptions> options) =>
        _maxAge = TimeSpan.FromHours(options.Value.SnapshotMaxAgeHours);

    public FeedSnapshot? Get()
    {
        var current = _snapshot;

        if (current is null) return null;

        var age = DateTimeOffset.UtcNow - current.CapturedAt;
        RecommendationMetrics.SnapshotAge.Set(age.TotalSeconds);

        return age > _maxAge ? null : current;
    }

    public void Update(IReadOnlyList<ArticleSummary> articles, FeedLevel producedAt)
    {
        if (articles.Count == 0) return;

        _snapshot = new FeedSnapshot(articles.ToList(), producedAt, DateTimeOffset.UtcNow);
        RecommendationMetrics.SnapshotAge.Set(0);
    }
}
