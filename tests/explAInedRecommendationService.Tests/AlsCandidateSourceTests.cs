using Xunit;

public class AlsCandidateSourceTests
{
    private static FeedRequest Request(string userId = "u1", int poolSize = 200) =>
        new(userId, Limit: 20, PoolSize: poolSize, Cursor: null);

    private static (AlsCandidateSource Source, InMemoryRecommendationStore Store) Build()
    {
        var store = new InMemoryRecommendationStore();
        return (new AlsCandidateSource(store), store);
    }

    [Fact]
    public void IsAPersonalizedSourceAlongsideFaiss()
    {
        var (source, _) = Build();

        Assert.Equal("als", source.Name);
        Assert.Equal(CandidateSourceKind.Personalized, source.Kind);
    }

    [Fact]
    public async Task MapsTheListIntoRankedCandidates()
    {
        var (source, store) = Build();
        store.SeedAlsCandidates("u1", "a", "b", "c");

        var batch = await source.FetchAsync(Request(), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ok, batch.Outcome);
        Assert.Equal(["a", "b", "c"], batch.Items.Select(i => i.ArticleId));
        Assert.Equal([0, 1, 2], batch.Items.Select(i => i.Rank));
        Assert.All(batch.Items, item => Assert.Equal("als", item.Source));
    }

    [Fact]
    public async Task ScoresDescendWithRankLikeTrendingDoes()
    {
        var (source, store) = Build();
        store.SeedAlsCandidates("u1", "a", "b", "c");

        var batch = await source.FetchAsync(Request(), CancellationToken.None);

        Assert.Equal(3d, batch.Items[0].Score);
        Assert.True(batch.Items[0].Score > batch.Items[1].Score);
    }

    [Fact]
    public async Task AsksForTheWholeCandidatePool()
    {
        var (source, store) = Build();
        store.SeedAlsCandidates("u1", Enumerable.Range(0, 300).Select(i => $"a{i}").ToArray());

        var batch = await source.FetchAsync(Request(poolSize: 150), CancellationToken.None);

        Assert.Equal(150, batch.Items.Count);
    }

    [Fact]
    public async Task AUserWithNoCandidatesIsDisabledNotAFailure()
    {
        var (source, _) = Build();

        var batch = await source.FetchAsync(Request("cold-start"), CancellationToken.None);

        Assert.Equal(SourceOutcome.Disabled, batch.Outcome);
        Assert.Empty(batch.Items);
    }

    [Fact]
    public async Task StoreFailuresBubbleUpForSourceExecutorToClassify()
    {
        var (source, store) = Build();
        store.SeedAlsCandidates("u1", "a");
        store.Faulted = true;

        await Assert.ThrowsAsync<RecommendationStoreException>(
            () => source.FetchAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task SlowReadsAreCutByTheSourceDeadline()
    {
        var (source, store) = Build();
        store.SeedAlsCandidates("u1", "a");
        store.ArtificialDelay = TimeSpan.FromMilliseconds(400);

        var (batch, outcome) = await TestFactories.Executor().RunAsync(
            source.Name,
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None,
            ct => source.FetchAsync(Request(), ct));

        Assert.Equal(SourceOutcome.Timeout, outcome);
        Assert.Null(batch);
    }
}
