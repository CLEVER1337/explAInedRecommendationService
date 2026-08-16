public sealed record Candidate(string ArticleId, double Score, string Source, int Rank);

public enum CandidateSourceKind
{
    Personalized,

    Global,
}

public sealed record CandidateBatch(
    string Source,
    CandidateSourceKind Kind,
    IReadOnlyList<Candidate> Items,
    SourceOutcome Outcome,
    string? Error = null)
{
    public static CandidateBatch Ok(string source, CandidateSourceKind kind, IReadOnlyList<Candidate> items) =>
        new(source, kind, items, items.Count == 0 ? SourceOutcome.Empty : SourceOutcome.Ok);

    public static CandidateBatch Failed(string source, CandidateSourceKind kind, SourceOutcome outcome, string? error = null) =>
        new(source, kind, Array.Empty<Candidate>(), outcome, error);
}
