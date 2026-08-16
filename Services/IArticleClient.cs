public interface IArticleClient
{
    Task<IReadOnlyList<ArticleSummary>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct);

    Task<IReadOnlyList<ArticleSummary>> GetRecentAsync(int limit, int offset, CancellationToken ct);
}
