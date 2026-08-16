using Microsoft.Extensions.Options;

public sealed class CandidateFeedStrategy : IFeedStrategy
{
    private readonly IReadOnlyList<ICandidateSource> _sources;
    private readonly IRanker _ranker;
    private readonly SourceExecutor _executor;
    private readonly FeedOptions _options;

    public CandidateFeedStrategy(
        IEnumerable<ICandidateSource> sources,
        IRanker ranker,
        SourceExecutor executor,
        IOptions<FeedOptions> options)
    {
        _sources = sources.ToList();
        _ranker = ranker;
        _executor = executor;
        _options = options.Value;
    }

    public FeedLevel Level => FeedLevel.Personalized;

    public async Task<FeedSlice?> TryBuildAsync(FeedContext context, CancellationToken ct)
    {
        if (_sources.Count == 0) return null;

        var deadline = TimeSpan.FromMilliseconds(_options.SourceDeadlineMs);

        var batches = await Task.WhenAll(_sources.Select(async source =>
        {
            var (batch, outcome) = await _executor.RunAsync(
                source.Name, deadline, ct, token => source.FetchAsync(context.Request, token));

            return outcome == SourceOutcome.Ok && batch is not null
                ? batch
                : CandidateBatch.Failed(source.Name, source.Kind, outcome);
        }));

        context.Batches.AddRange(batches);

        var personalized = batches.Where(b => b.Kind == CandidateSourceKind.Personalized).ToList();
        if (personalized.Sum(b => b.Items.Count) == 0) return null;

        var perSource = batches.Where(b => b.Items.Count > 0).Select(b => b.Items).ToList();
        var pool = Interleaver.RoundRobin(perSource, context.Excluded, _options.CandidatePoolSize);
        if (pool.Count == 0) return null;

        var (ranked, rankOutcome) = await _executor.RunAsync(
            _ranker.Name,
            TimeSpan.FromMilliseconds(_options.RankerDeadlineMs),
            ct,
            token => _ranker.RankAsync(context.Request.UserId, pool, token));

        if (rankOutcome != SourceOutcome.Ok || ranked is null || !ranked.Succeeded)
        {
            return new FeedSlice(FeedLevel.Unranked, pool, ranked?.Error ?? rankOutcome.ToWire());
        }

        var degraded = batches.Any(b => b.Outcome is SourceOutcome.Timeout or SourceOutcome.Faulted or SourceOutcome.Unavailable);

        return new FeedSlice(degraded ? FeedLevel.Partial : FeedLevel.Personalized, ranked.Items);
    }
}
