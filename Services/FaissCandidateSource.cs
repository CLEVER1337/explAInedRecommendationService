public sealed class FaissCandidateSource : ICandidateSource
{
    public const string SourceName = "faiss";

    private readonly IFaissClient _client;

    public FaissCandidateSource(IFaissClient client) => _client = client;

    public string Name => SourceName;

    public CandidateSourceKind Kind => CandidateSourceKind.Personalized;

    public async Task<CandidateBatch> FetchAsync(FeedRequest request, CancellationToken ct)
    {
        var result = await _client.SearchAsync(request.UserId, request.PoolSize, ct);

        // No user embedding: the source is off for this user, which is not a failure and must not
        // count as degradation. Everything else (timeouts, 5xx) is classified by SourceExecutor.
        if (result is null)
        {
            return CandidateBatch.Failed(SourceName, Kind, SourceOutcome.Disabled, "no user embedding");
        }

        var items = result.Ids
            .Select((id, index) => new Candidate(
                id,
                Score: index < result.Scores.Count ? result.Scores[index] : 0d,
                SourceName,
                Rank: index))
            .ToList();

        return CandidateBatch.Ok(SourceName, Kind, items);
    }
}
