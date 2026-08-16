public enum FeedLevel
{
    Personalized = 0,
    Partial = 1,
    Unranked = 2,
    Trending = 3,
    Recent = 4,
    Cached = 5,
    Empty = 6,
}

public static class FeedLevelExtensions
{
    public static string ToWire(this FeedLevel level) => level.ToString().ToLowerInvariant();
}
