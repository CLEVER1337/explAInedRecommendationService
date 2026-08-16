using System.Diagnostics;
using Microsoft.Extensions.Options;

public sealed class FeedOrchestrator
{
    public const string ArticleBatchSource = "article-batch";
    public const string ViewedSource = "viewed";
    public const string PageCacheSource = "page-cache";

    private readonly IReadOnlyList<IFeedStrategy> _strategies;
    private readonly IRecommendationStore _store;
    private readonly IArticleClient _articles;
    private readonly IFeedSnapshotStore _snapshots;
    private readonly IImpressionLog _impressions;
    private readonly SourceExecutor _executor;
    private readonly FeedOptions _options;
    private readonly ILogger<FeedOrchestrator> _logger;

    public FeedOrchestrator(
        IEnumerable<IFeedStrategy> strategies,
        IRecommendationStore store,
        IArticleClient articles,
        IFeedSnapshotStore snapshots,
        IImpressionLog impressions,
        SourceExecutor executor,
        IOptions<FeedOptions> options,
        ILogger<FeedOrchestrator> logger)
    {
        _strategies = strategies.OrderBy(s => (int)s.Level).ToList();
        _store = store;
        _articles = articles;
        _snapshots = snapshots;
        _impressions = impressions;
        _executor = executor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FeedResult> GetFeedAsync(string userId, int limit, string? rawCursor, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(_options.TotalBudgetMs));

        FeedCursor.TryDecode(rawCursor, out var cursor);

        var result = cursor is null
            ? await BuildAsync(userId, limit, pinned: null, rebuilt: false, budget.Token)
            : await ContinueAsync(userId, limit, cursor, budget.Token);

        RecommendationMetrics.Requests.WithLabels(result.Level.ToWire()).Inc();
        RecommendationMetrics.ItemsReturned.Observe(result.Items.Count);
        RecommendationMetrics.BuildDuration.WithLabels(result.Level.ToWire())
            .Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
        RecommendationMetrics.PagesServed
            .WithLabels(cursor is null ? "first" : result.Rebuilt ? "rebuilt" : "cursor", result.Level.ToWire())
            .Inc();

        return result;
    }

    private async Task<FeedResult> ContinueAsync(string userId, int limit, FeedCursor cursor, CancellationToken budget)
    {
        var (pageSet, outcome) = await _executor.RunAsync(
            PageCacheSource,
            TimeSpan.FromMilliseconds(_options.SourceDeadlineMs),
            budget,
            token => _store.GetFeedPageSetAsync(userId, cursor.FeedId, token));

        if (outcome != SourceOutcome.Ok || pageSet is null || pageSet.Count == 0)
        {
            RecommendationMetrics.PageCache.WithLabels(outcome == SourceOutcome.Ok ? "miss" : "error").Inc();

            return await BuildAsync(userId, limit, pinned: cursor.Level, rebuilt: true, budget);
        }

        RecommendationMetrics.PageCache.WithLabels("hit").Inc();

        var window = pageSet.Skip(cursor.Offset).Take(limit).ToList();
        var hydration = await HydrateAsync(window, cursor.Level, budget);

        var level = hydration.FromSnapshot && cursor.Level < FeedLevel.Cached ? FeedLevel.Cached : cursor.Level;

        var nextOffset = cursor.Offset + window.Count;
        var next = nextOffset < pageSet.Count
            ? FeedCursor.Encode(FeedCursor.Create(cursor.FeedId, cursor.Level, nextOffset))
            : null;

        return new FeedResult(hydration.Items, level, cursor.FeedId, next, null, Rebuilt: false, Array.Empty<SourceDiagnosticDto>());
    }

    private async Task<FeedResult> BuildAsync(string userId, int limit, FeedLevel? pinned, bool rebuilt, CancellationToken budget)
    {
        var request = new FeedRequest(userId, limit, _options.CandidatePoolSize, null);
        var context = new FeedContext(request) { PinnedLevel = pinned };

        var pool = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        FeedLevel? level = null;
        string? reason = null;

        foreach (var strategy in _strategies)
        {
            if (pinned is not null && strategy.Level < pinned) continue;
            if (pool.Count >= limit) break;

            var slice = await strategy.TryBuildAsync(context, budget);
            if (slice is null) continue;

            var fresh = await RemoveViewedAsync(userId, slice.Candidates, context, budget);
            fresh = fresh.Where(c => seen.Add(c.ArticleId)).ToList();

            if (fresh.Count == 0)
            {
                if (slice.Level == FeedLevel.Empty)
                {
                    if (level is null)
                    {
                        level = FeedLevel.Empty;
                        reason = slice.Reason;
                    }

                    break;
                }

                continue;
            }

            if (level is null)
            {
                level = slice.Level;
                reason = slice.Reason;
            }
            else
            {
                RecommendationMetrics.Backfill.WithLabels(level.Value.ToWire(), slice.Level.ToWire()).Inc();
            }

            pool.AddRange(fresh);
        }

        level ??= FeedLevel.Empty;

        var feedId = Guid.NewGuid().ToString("N");

        var hydrationWindow = pool.Take(Math.Min(pool.Count, Math.Max(limit * 2, limit))).Select(c => c.ArticleId).ToList();
        var hydration = await HydrateAsync(hydrationWindow, level.Value, budget);
        var items = hydration.Items.Take(limit).ToList();

        if (hydration.FromSnapshot && level < FeedLevel.Cached) level = FeedLevel.Cached;

        if (items.Count == 0 && level != FeedLevel.Empty)
        {
            reason ??= "candidates could not be hydrated";
            level = FeedLevel.Empty;
        }

        var ids = pool.Select(c => c.ArticleId).ToList();
        string? next = null;

        if (ids.Count > items.Count && items.Count > 0)
        {
            await SavePageSetAsync(userId, feedId, ids, budget);
            next = FeedCursor.Encode(FeedCursor.Create(feedId, level.Value, items.Count));
        }

        await LogImpressionAsync(userId, feedId, items, level.Value, budget);

        return new FeedResult(items, level.Value, feedId, next, reason, rebuilt, Diagnostics(context));
    }

    private async Task<IReadOnlyList<Candidate>> RemoveViewedAsync(
        string userId, IReadOnlyList<Candidate> candidates, FeedContext context, CancellationToken budget)
    {
        if (candidates.Count == 0) return candidates;

        var ids = candidates.Select(c => c.ArticleId).ToList();

        var (viewed, outcome) = await _executor.RunAsync(
            ViewedSource,
            TimeSpan.FromMilliseconds(_options.SourceDeadlineMs),
            budget,
            token => _store.GetViewedSubsetAsync(userId, ids, token));

        if (outcome != SourceOutcome.Ok || viewed is null || viewed.Count == 0) return candidates;

        foreach (var id in viewed) context.Excluded.Add(id);

        return candidates.Where(c => !viewed.Contains(c.ArticleId)).ToList();
    }

    private async Task<HydrationResult> HydrateAsync(IReadOnlyList<string> ids, FeedLevel level, CancellationToken budget)
    {
        if (ids.Count == 0) return new HydrationResult(Array.Empty<FeedItemDto>(), FromSnapshot: false);

        var (articles, outcome) = await _executor.RunAsync(
            ArticleBatchSource,
            TimeSpan.FromMilliseconds(_options.ArticleDeadlineMs),
            budget,
            token => _articles.GetByIdsAsync(ids, token));

        var map = new Dictionary<string, ArticleSummary>(StringComparer.Ordinal);

        if (outcome == SourceOutcome.Ok && articles is not null)
        {
            foreach (var article in articles) map[article.Id] = article;
        }

        if (map.Count < ids.Count)
        {
            var snapshot = _snapshots.Get();

            if (snapshot is not null)
            {
                foreach (var article in snapshot.Articles) map.TryAdd(article.Id, article);
            }
        }

        var articleList = ids.Where(map.ContainsKey).Select(id => map[id]).ToList();

        var fromSnapshot = outcome != SourceOutcome.Ok && articleList.Count > 0;
        var effective = fromSnapshot && level < FeedLevel.Cached ? FeedLevel.Cached : level;

        var items = articleList
            .Select(a => new FeedItemDto(a.Id, a.Title, a.Description, a.Tags, a.AuthorId, a.PublishedAt, a.WordCount, effective.ToWire()))
            .ToList();

        if (effective <= FeedLevel.Trending && outcome == SourceOutcome.Ok && items.Count > 0)
        {
            _snapshots.Update(articleList, effective);
        }

        return new HydrationResult(items, fromSnapshot);
    }

    private sealed record HydrationResult(IReadOnlyList<FeedItemDto> Items, bool FromSnapshot);

    private async Task SavePageSetAsync(string userId, string feedId, IReadOnlyList<string> ids, CancellationToken budget) =>
        await _executor.RunAsync<bool>(
            PageCacheSource,
            TimeSpan.FromMilliseconds(_options.SourceDeadlineMs),
            budget,
            async token =>
            {
                await _store.SaveFeedPageSetAsync(userId, feedId, ids, TimeSpan.FromSeconds(_options.FeedCacheTtlSeconds), token);
                return true;
            });

    private async Task LogImpressionAsync(string userId, string feedId, IReadOnlyList<FeedItemDto> items, FeedLevel level, CancellationToken budget)
    {
        if (items.Count == 0) return;

        try
        {
            var impression = new FeedImpression(
                feedId, userId, items.Select(i => i.Id).ToList(), level, DateTimeOffset.UtcNow);

            await _impressions.LogAsync(impression, budget);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impression logging failed for feed {FeedId}", feedId);
        }
    }

    private static IReadOnlyList<SourceDiagnosticDto> Diagnostics(FeedContext context) =>
        context.Batches.Select(b => new SourceDiagnosticDto(b.Source, b.Outcome.ToWire())).ToList();
}
