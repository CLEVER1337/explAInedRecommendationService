public sealed record FeedResponseDto(
    IReadOnlyList<FeedItemDto> Items,
    string Level,
    string FeedId,
    string? NextCursor,
    string? Reason,
    bool Degraded,
    IReadOnlyList<SourceDiagnosticDto> Sources);

public sealed record FeedItemDto(
    string Id,
    string Title,
    string Description,
    string Tags,
    string AuthorId,
    DateTime PublishedAt,
    int WordCount,
    string Source);

public sealed record SourceDiagnosticDto(string Name, string Outcome);
