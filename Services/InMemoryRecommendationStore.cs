using System.Collections.Concurrent;

public sealed class InMemoryRecommendationStore : IRecommendationStore
{
    private readonly List<string> _trending = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _viewed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _pages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _als = new(StringComparer.Ordinal);

    public bool Faulted { get; set; }

    public TimeSpan ArtificialDelay { get; set; } = TimeSpan.Zero;

    public void SeedTrending(params string[] ids)
    {
        lock (_trending)
        {
            _trending.Clear();
            _trending.AddRange(ids);
        }
    }

    public void SeedViewed(string userId, params string[] ids) =>
        _viewed[userId] = new HashSet<string>(ids, StringComparer.Ordinal);

    public void SeedAlsCandidates(string userId, params string[] ids) => _als[userId] = ids.ToList();

    public async Task<IReadOnlyList<string>> GetTrendingAsync(int count, CancellationToken ct)
    {
        await GateAsync(ct);

        lock (_trending)
        {
            return _trending.Take(count).ToList();
        }
    }

    public async Task<IReadOnlyList<string>> GetAlsCandidatesAsync(
        string userId, int count, CancellationToken ct)
    {
        await GateAsync(ct);

        return _als.TryGetValue(userId, out var ids) ? ids.Take(count).ToList() : [];
    }

    public async Task<IReadOnlySet<string>> GetViewedSubsetAsync(
        string userId, IReadOnlyCollection<string> candidateIds, CancellationToken ct)
    {
        await GateAsync(ct);

        if (!_viewed.TryGetValue(userId, out var seen))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return new HashSet<string>(candidateIds.Where(seen.Contains), StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>?> GetFeedPageSetAsync(string userId, string feedId, CancellationToken ct)
    {
        await GateAsync(ct);

        return _pages.TryGetValue(PageKey(userId, feedId), out var ids) ? ids : null;
    }

    public async Task SaveFeedPageSetAsync(
        string userId, string feedId, IReadOnlyList<string> ids, TimeSpan ttl, CancellationToken ct)
    {
        await GateAsync(ct);

        _pages[PageKey(userId, feedId)] = ids.ToList();
    }

    private async Task GateAsync(CancellationToken ct)
    {
        if (ArtificialDelay > TimeSpan.Zero) await Task.Delay(ArtificialDelay, ct);
        if (Faulted) throw new RecommendationStoreException("in-memory store faulted");
    }

    private static string PageKey(string userId, string feedId) => $"{userId}:{feedId}";
}
