public interface ICandidateSource
{
    string Name { get; }

    CandidateSourceKind Kind { get; }

    Task<CandidateBatch> FetchAsync(FeedRequest request, CancellationToken ct);
}

public sealed record RankResult(IReadOnlyList<Candidate> Items, bool Succeeded, string? Error = null);

public interface IRanker
{
    string Name { get; }

    Task<RankResult> RankAsync(string userId, IReadOnlyList<Candidate> candidates, CancellationToken ct);
}

public sealed class NullRanker : IRanker
{
    public string Name => "null-ranker";

    public Task<RankResult> RankAsync(string userId, IReadOnlyList<Candidate> candidates, CancellationToken ct) =>
        Task.FromResult(new RankResult(candidates, Succeeded: false, "no ranker configured"));
}
