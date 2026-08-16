using Microsoft.Extensions.Logging.Abstractions;

public class FeedOrchestratorTests
{
    private const string UserId = "user-1";

    private readonly InMemoryRecommendationStore _store = new();
    private readonly FakeArticleClient _articles = new();
    private readonly StubRanker _ranker = new();
    private readonly List<ICandidateSource> _personalizedSources = new();

    private InMemoryFeedSnapshotStore _snapshots = null!;

    private FeedOrchestrator Build(Action<FeedOptions>? configure = null)
    {
        var options = TestFactories.Options(configure);
        var executor = TestFactories.Executor();

        _snapshots = new InMemoryFeedSnapshotStore(options);

        var trending = new TrendingCandidateSource(_store);
        var sources = _personalizedSources.Append<ICandidateSource>(trending).ToList();

        var strategies = new IFeedStrategy[]
        {
            new CandidateFeedStrategy(sources, _ranker, executor, options),
            new TrendingFeedStrategy(trending, executor, options),
            new RecentFeedStrategy(_articles, executor, options),
            new CachedSnapshotFeedStrategy(_snapshots),
            new EmptyFeedStrategy(),
        };

        return new FeedOrchestrator(
            strategies, _store, _articles, _snapshots, new NoOpImpressionLog(),
            executor, options, NullLogger<FeedOrchestrator>.Instance);
    }

    [Fact]
    public async Task Serves_Trending_WhenStoreHasList()
    {
        _store.SeedTrending("a", "b", "c");
        _articles.SeedMany("a", "b", "c");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Trending, result.Level);
        Assert.Equal(new[] { "a", "b", "c" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task FallsBackToRecent_WhenStoreIsDown()
    {
        _store.Faulted = true;
        _articles.SeedMany("r1", "r2");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Recent, result.Level);
        Assert.Equal(new[] { "r1", "r2" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task FallsBackToSnapshot_WhenStoreAndArticleServiceAreDown()
    {
        _store.SeedTrending("a", "b");
        _articles.SeedMany("a", "b");

        var orchestrator = Build();
        await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        _store.Faulted = true;
        _articles.Faulted = true;

        var result = await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Cached, result.Level);
        Assert.Equal(new[] { "a", "b" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ReportsCached_WhenOrderingIsLive_ButContentComesFromSnapshot()
    {
        _store.SeedTrending("a", "b");
        _articles.SeedMany("a", "b");

        var orchestrator = Build();
        await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        _articles.Faulted = true;

        var result = await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Cached, result.Level);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.Equal("cached", i.Source));
    }

    [Fact]
    public async Task FallsBackToEmpty_WhenSnapshotIsCold()
    {
        _store.Faulted = true;
        _articles.Faulted = true;

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Empty, result.Level);
        Assert.Empty(result.Items);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public async Task Snapshot_IsIgnored_WhenTooOld()
    {
        _store.SeedTrending("a");
        _articles.SeedMany("a");

        var orchestrator = Build(o => o.SnapshotMaxAgeHours = 0);
        await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        _store.Faulted = true;
        _articles.Faulted = true;

        var result = await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Empty, result.Level);
    }

    [Fact]
    public async Task CachedResponse_DoesNotRefreshSnapshotAge()
    {
        _store.SeedTrending("a");
        _articles.SeedMany("a");

        var orchestrator = Build();
        await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        var capturedAt = _snapshots.Get()!.CapturedAt;

        _store.Faulted = true;
        _articles.Faulted = true;
        await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(capturedAt, _snapshots.Get()!.CapturedAt);
    }

    [Fact]
    public async Task FiltersViewedArticles_AndBackfillsFromNextRung()
    {
        _store.SeedTrending("seen1", "seen2");
        _store.SeedViewed(UserId, "seen1", "seen2");
        _articles.SeedMany("seen1", "seen2", "fresh1", "fresh2");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.DoesNotContain(result.Items, i => i.Id.StartsWith("seen"));
        Assert.Equal(FeedLevel.Recent, result.Level);
        Assert.Contains(result.Items, i => i.Id == "fresh1");
    }

    [Fact]
    public async Task ReportsTrending_WhenTrendingHeadsTheFeed_EvenIfBackfilled()
    {
        _store.SeedTrending("t1");
        _articles.SeedMany("t1", "r1", "r2");

        var result = await Build().GetFeedAsync(UserId, 3, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Trending, result.Level);
        Assert.Equal("t1", result.Items[0].Id);
        Assert.True(result.Items.Count > 1, "expected a backfill from the recent rung");
    }

    [Fact]
    public async Task DropsCandidatesThatCannotBeHydrated()
    {
        _store.SeedTrending("archived", "alive");
        _articles.SeedMany("alive");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(new[] { "alive" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ServedFeed_HasNoReason_FromTheTerminalRung()
    {
        _articles.SeedMany("r1");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Recent, result.Level);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task Diagnostics_ReportServingRungAsOk()
    {
        _articles.SeedMany("r1", "r2");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        var recent = Assert.Single(result.Sources, s => s.Name == RecentFeedStrategy.SourceName);
        Assert.Equal("ok", recent.Outcome);
    }

    [Fact]
    public async Task Unranked_WhenPersonalizedSourcesExist_ButRankerFails()
    {
        _personalizedSources.Add(new FakeCandidateSource("als", CandidateSourceKind.Personalized, "p1", "p2"));
        _ranker.Succeeds = false;
        _articles.SeedMany("p1", "p2");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Unranked, result.Level);
        Assert.Equal(new[] { "p1", "p2" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Personalized_WhenEverySourceAndTheRankerSucceed()
    {
        _personalizedSources.Add(new FakeCandidateSource("als", CandidateSourceKind.Personalized, "p1", "p2"));
        _articles.SeedMany("p1", "p2");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Personalized, result.Level);
        Assert.Equal(new[] { "p2", "p1" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Partial_WhenOneSourceFails_ButRankerSucceeds()
    {
        _personalizedSources.Add(new FakeCandidateSource("als", CandidateSourceKind.Personalized, "p1"));
        _personalizedSources.Add(new FakeCandidateSource("faiss", CandidateSourceKind.Personalized) { Faulted = true });
        _articles.SeedMany("p1");

        var result = await Build().GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Partial, result.Level);
    }

    [Fact]
    public async Task SlowSourceIsTimedOut_AndHealthySourceStillServes()
    {
        _store.ArtificialDelay = TimeSpan.FromMilliseconds(500);
        _articles.SeedMany("r1");

        var result = await Build(o =>
        {
            o.SourceDeadlineMs = 50;
            o.TotalBudgetMs = 2000;
        }).GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.Equal(FeedLevel.Recent, result.Level);
        Assert.Equal(new[] { "r1" }, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task SecondPage_KeepsLevel_AndDoesNotRepeatItems()
    {
        _store.SeedTrending("a", "b", "c", "d");
        _articles.SeedMany("a", "b", "c", "d");

        var orchestrator = Build();
        var first = await orchestrator.GetFeedAsync(UserId, 2, null, CancellationToken.None);

        Assert.NotNull(first.NextCursor);

        var second = await orchestrator.GetFeedAsync(UserId, 2, first.NextCursor, CancellationToken.None);

        Assert.Equal(first.Level, second.Level);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
        Assert.Equal(new[] { "c", "d" }, second.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task RebuildOnLostPageSet_CanOnlyDegrade()
    {
        _store.SeedTrending("a", "b", "c");
        _articles.SeedMany("a", "b", "c", "r1");

        var orchestrator = Build();
        var first = await orchestrator.GetFeedAsync(UserId, 2, null, CancellationToken.None);

        _store.Faulted = true;

        var second = await orchestrator.GetFeedAsync(UserId, 2, first.NextCursor, CancellationToken.None);

        Assert.True(second.Rebuilt);
        Assert.True(second.Level >= first.Level);
    }

    [Fact]
    public async Task GarbageCursor_BuildsAFreshFeed()
    {
        _store.SeedTrending("a");
        _articles.SeedMany("a");

        var result = await Build().GetFeedAsync(UserId, 20, "!!!not-a-cursor!!!", CancellationToken.None);

        Assert.Equal(FeedLevel.Trending, result.Level);
        Assert.Single(result.Items);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task NeverThrows_ForAnyCombinationOfOutages(bool storeDown, bool articlesDown, bool warmSnapshot)
    {
        _store.SeedTrending("a", "b");
        _articles.SeedMany("a", "b");

        var orchestrator = Build();

        if (warmSnapshot)
        {
            await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);
        }

        _store.Faulted = storeDown;
        _articles.Faulted = articlesDown;

        var result = await orchestrator.GetFeedAsync(UserId, 20, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.FeedId);
        Assert.True(result.Items.Count > 0 || result.Level == FeedLevel.Empty);
    }
}
