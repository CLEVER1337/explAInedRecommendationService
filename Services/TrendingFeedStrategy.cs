using Microsoft.Extensions.Options;

public sealed class TrendingFeedStrategy : IFeedStrategy
{
    private readonly TrendingCandidateSource _trending;
    private readonly SourceExecutor _executor;
    private readonly FeedOptions _options;

    public TrendingFeedStrategy(TrendingCandidateSource trending, SourceExecutor executor, IOptions<FeedOptions> options)
    {
        _trending = trending;
        _executor = executor;
        _options = options.Value;
    }

    public FeedLevel Level => FeedLevel.Trending;

    public async Task<FeedSlice?> TryBuildAsync(FeedContext context, CancellationToken ct)
    {
        var existing = context.Batches.FirstOrDefault(b => b.Source == TrendingCandidateSource.SourceName);

        if (existing is null)
        {
            var (batch, outcome) = await _executor.RunAsync(
                _trending.Name,
                TimeSpan.FromMilliseconds(_options.SourceDeadlineMs),
                ct,
                token => _trending.FetchAsync(context.Request, token));

            existing = outcome == SourceOutcome.Ok && batch is not null
                ? batch
                : CandidateBatch.Failed(_trending.Name, _trending.Kind, outcome);

            context.Batches.Add(existing);
        }

        if (existing.Outcome != SourceOutcome.Ok || existing.Items.Count == 0) return null;

        var items = existing.Items.Where(c => !context.Excluded.Contains(c.ArticleId)).ToList();

        return items.Count == 0 ? null : new FeedSlice(FeedLevel.Trending, items);
    }
}
