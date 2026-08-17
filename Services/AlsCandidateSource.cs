public sealed class AlsCandidateSource : ICandidateSource
{
    public const string SourceName = "als";

    private readonly IRecommendationStore _store;

    public AlsCandidateSource(IRecommendationStore store) => _store = store;

    public string Name => SourceName;

    public CandidateSourceKind Kind => CandidateSourceKind.Personalized;

    public async Task<CandidateBatch> FetchAsync(FeedRequest request, CancellationToken ct)
    {
        var ids = await _store.GetAlsCandidatesAsync(request.UserId, request.PoolSize, ct);

        if (ids.Count == 0)
        {
            return CandidateBatch.Failed(SourceName, Kind, SourceOutcome.Disabled, "no als candidates");
        }

        var items = ids
            .Select((id, index) => new Candidate(id, Score: ids.Count - index, SourceName, Rank: index))
            .ToList();

        return CandidateBatch.Ok(SourceName, Kind, items);
    }
}
