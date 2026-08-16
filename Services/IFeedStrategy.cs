public interface IFeedStrategy
{
    FeedLevel Level { get; }

    Task<FeedSlice?> TryBuildAsync(FeedContext context, CancellationToken ct);
}
