public interface IImpressionLog
{
    Task LogAsync(FeedImpression impression, CancellationToken ct);
}

public sealed record FeedImpression(
    string FeedId,
    string UserId,
    IReadOnlyList<string> ArticleIds,
    FeedLevel Level,
    DateTimeOffset ServedAt);

public sealed class NoOpImpressionLog : IImpressionLog
{
    public Task LogAsync(FeedImpression impression, CancellationToken ct) => Task.CompletedTask;
}
