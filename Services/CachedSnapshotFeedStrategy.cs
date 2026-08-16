public sealed class CachedSnapshotFeedStrategy : IFeedStrategy
{
    public const string SourceName = "snapshot";

    private readonly IFeedSnapshotStore _snapshots;

    public CachedSnapshotFeedStrategy(IFeedSnapshotStore snapshots) => _snapshots = snapshots;

    public FeedLevel Level => FeedLevel.Cached;

    public Task<FeedSlice?> TryBuildAsync(FeedContext context, CancellationToken ct)
    {
        var snapshot = _snapshots.Get();

        if (snapshot is null)
        {
            context.Batches.Add(CandidateBatch.Failed(SourceName, CandidateSourceKind.Global, SourceOutcome.Empty));
            return Task.FromResult<FeedSlice?>(null);
        }

        var items = snapshot.Articles
            .Where(a => !context.Excluded.Contains(a.Id))
            .Select((a, index) => new Candidate(a.Id, Score: snapshot.Articles.Count - index, SourceName, Rank: index))
            .ToList();

        context.Batches.Add(CandidateBatch.Ok(SourceName, CandidateSourceKind.Global, items));

        return Task.FromResult(items.Count == 0 ? null : new FeedSlice(FeedLevel.Cached, items));
    }
}

public sealed class EmptyFeedStrategy : IFeedStrategy
{
    public FeedLevel Level => FeedLevel.Empty;

    public Task<FeedSlice?> TryBuildAsync(FeedContext context, CancellationToken ct)
    {
        var reason = context.Batches.Count == 0
            ? "no candidate source produced anything"
            : string.Join(", ", context.Batches.Select(b => $"{b.Source}={b.Outcome.ToWire()}"));

        return Task.FromResult<FeedSlice?>(new FeedSlice(FeedLevel.Empty, Array.Empty<Candidate>(), reason));
    }
}
