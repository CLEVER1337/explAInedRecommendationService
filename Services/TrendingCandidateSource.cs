public sealed class TrendingCandidateSource : ICandidateSource
{
    public const string SourceName = "trending";

    private readonly IRecommendationStore _store;

    public TrendingCandidateSource(IRecommendationStore store) => _store = store;

    public string Name => SourceName;

    public CandidateSourceKind Kind => CandidateSourceKind.Global;

    public async Task<CandidateBatch> FetchAsync(FeedRequest request, CancellationToken ct)
    {
        var ids = await _store.GetTrendingAsync(request.PoolSize, ct);

        var items = ids
            .Select((id, index) => new Candidate(id, Score: ids.Count - index, SourceName, Rank: index))
            .ToList();

        return CandidateBatch.Ok(SourceName, Kind, items);
    }
}
