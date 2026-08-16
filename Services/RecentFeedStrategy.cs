using Microsoft.Extensions.Options;

public sealed class RecentFeedStrategy : IFeedStrategy
{
    public const string SourceName = "article-recent";

    private readonly IArticleClient _articles;
    private readonly SourceExecutor _executor;
    private readonly FeedOptions _options;

    public RecentFeedStrategy(IArticleClient articles, SourceExecutor executor, IOptions<FeedOptions> options)
    {
        _articles = articles;
        _executor = executor;
        _options = options.Value;
    }

    public FeedLevel Level => FeedLevel.Recent;

    public async Task<FeedSlice?> TryBuildAsync(FeedContext context, CancellationToken ct)
    {
        var take = Math.Min(_options.CandidatePoolSize, Math.Max(_options.MaxLimit, context.Request.Limit * 4));

        var (articles, outcome) = await _executor.RunAsync(
            SourceName,
            TimeSpan.FromMilliseconds(_options.ArticleDeadlineMs),
            ct,
            token => _articles.GetRecentAsync(take, 0, token));

        if (outcome != SourceOutcome.Ok || articles is null)
        {
            context.Batches.Add(CandidateBatch.Failed(SourceName, CandidateSourceKind.Global, outcome));
            return null;
        }

        var items = articles
            .Where(a => !context.Excluded.Contains(a.Id))
            .Select((a, index) => new Candidate(a.Id, Score: articles.Count - index, SourceName, Rank: index))
            .ToList();

        context.Batches.Add(CandidateBatch.Ok(SourceName, CandidateSourceKind.Global, items));

        return items.Count == 0 ? null : new FeedSlice(FeedLevel.Recent, items);
    }
}
