public interface IRecommendationStore
{
    Task<IReadOnlyList<string>> GetTrendingAsync(int count, CancellationToken ct);

    Task<IReadOnlySet<string>> GetViewedSubsetAsync(string userId, IReadOnlyCollection<string> candidateIds, CancellationToken ct);

    Task<IReadOnlyList<string>?> GetFeedPageSetAsync(string userId, string feedId, CancellationToken ct);

    Task SaveFeedPageSetAsync(string userId, string feedId, IReadOnlyList<string> ids, TimeSpan ttl, CancellationToken ct);
}

public sealed class RecommendationStoreException : Exception
{
    public RecommendationStoreException(string message) : base(message) { }

    public RecommendationStoreException(string message, Exception inner) : base(message, inner) { }
}
