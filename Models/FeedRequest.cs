public sealed record FeedRequest(string UserId, int Limit, int PoolSize, FeedCursor? Cursor);

public sealed class FeedContext
{
    public FeedContext(FeedRequest request) => Request = request;

    public FeedRequest Request { get; }

    public List<CandidateBatch> Batches { get; } = new();

    public HashSet<string> Excluded { get; } = new(StringComparer.Ordinal);

    public FeedLevel? PinnedLevel { get; set; }
}

public sealed record FeedSlice(FeedLevel Level, IReadOnlyList<Candidate> Candidates, string? Reason = null);

public sealed record FeedSnapshot(IReadOnlyList<ArticleSummary> Articles, FeedLevel SourceLevel, DateTimeOffset CapturedAt);

public sealed record FeedResult(
    IReadOnlyList<FeedItemDto> Items,
    FeedLevel Level,
    string FeedId,
    string? NextCursor,
    string? Reason,
    bool Rebuilt,
    IReadOnlyList<SourceDiagnosticDto> Sources)
{
    public bool Degraded => Level >= FeedLevel.Unranked;
}

public sealed record ArticleSummary(
    string Id,
    string Title,
    string Description,
    string Tags,
    string AuthorId,
    DateTime PublishedAt,
    int WordCount);
