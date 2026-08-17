using StackExchange.Redis;

public sealed class RedisRecommendationStore : IRecommendationStore
{
    public const string TrendingKey = "rec:trending:top100";

    private readonly IConnectionMultiplexer _mux;

    public RedisRecommendationStore(IConnectionMultiplexer mux) => _mux = mux;

    public async Task<IReadOnlyList<string>> GetTrendingAsync(int count, CancellationToken ct)
    {
        var db = Database();
        var values = await db.ListRangeAsync(TrendingKey, 0, count - 1);

        return values.Select(v => v.ToString()).Where(v => !string.IsNullOrEmpty(v)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetAlsCandidatesAsync(
        string userId, int count, CancellationToken ct)
    {
        var db = Database();
        var values = await db.ListRangeAsync(AlsKey(userId), 0, count - 1);

        return values.Select(v => v.ToString()).Where(v => !string.IsNullOrEmpty(v)).ToList();
    }

    public async Task<IReadOnlySet<string>> GetViewedSubsetAsync(
        string userId, IReadOnlyCollection<string> candidateIds, CancellationToken ct)
    {
        if (candidateIds.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        var db = Database();
        var ids = candidateIds.ToArray();
        var members = ids.Select(id => (RedisValue)id).ToArray();

        var flags = await db.SetContainsAsync(ViewedKey(userId), members);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Length && i < flags.Length; i++)
        {
            if (flags[i]) seen.Add(ids[i]);
        }

        return seen;
    }

    public async Task<IReadOnlyList<string>?> GetFeedPageSetAsync(string userId, string feedId, CancellationToken ct)
    {
        var db = Database();
        var values = await db.ListRangeAsync(FeedKey(userId, feedId));

        if (values.Length == 0) return null;

        return values.Select(v => v.ToString()).Where(v => !string.IsNullOrEmpty(v)).ToList();
    }

    public async Task SaveFeedPageSetAsync(
        string userId, string feedId, IReadOnlyList<string> ids, TimeSpan ttl, CancellationToken ct)
    {
        if (ids.Count == 0) return;

        var db = Database();
        var key = FeedKey(userId, feedId);

        await db.ListRightPushAsync(key, ids.Select(id => (RedisValue)id).ToArray());
        await db.KeyExpireAsync(key, ttl);
    }

    private IDatabase Database()
    {
        if (!_mux.IsConnected) throw new RecommendationStoreException("redis disconnected");

        return _mux.GetDatabase();
    }

    private static RedisKey AlsKey(string userId) => $"rec:user_als_candidates:{userId}";

    private static RedisKey ViewedKey(string userId) => $"rec:user_viewed:{userId}";

    private static RedisKey FeedKey(string userId, string feedId) => $"rec:feed:{userId}:{feedId}";
}
