public class FeedOptions
{
    public int DefaultLimit { get; set; } = 20;
    public int MaxLimit { get; set; } = 50;
    public int CandidatePoolSize { get; set; } = 200;

    public int TotalBudgetMs { get; set; } = 300;
    public int SourceDeadlineMs { get; set; } = 120;
    public int RankerDeadlineMs { get; set; } = 120;
    public int ArticleDeadlineMs { get; set; } = 150;

    public int FeedCacheTtlSeconds { get; set; } = 300;
    public int SnapshotMaxAgeHours { get; set; } = 24;
}
