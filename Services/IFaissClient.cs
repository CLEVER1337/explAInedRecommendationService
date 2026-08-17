public interface IFaissClient
{
    /// <summary>
    /// Content-based candidates for a user. Returns <c>null</c> when the user has no embedding yet —
    /// a cold-start state the FAISS service reports as 204, not an error.
    /// </summary>
    Task<FaissSearchResult?> SearchAsync(string userId, int topK, CancellationToken ct);
}

public sealed record FaissSearchResult(IReadOnlyList<string> Ids, IReadOnlyList<double> Scores);
