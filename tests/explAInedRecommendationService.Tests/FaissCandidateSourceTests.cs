using Xunit;

public class FaissCandidateSourceTests
{
    private static FeedRequest Request(string userId = "u1", int poolSize = 200) =>
        new(userId, Limit: 20, PoolSize: poolSize, Cursor: null);

    [Fact]
    public void IsThePersonalizedSourceThatWakesTheUpperLevels()
    {
        var source = new FaissCandidateSource(new FakeFaissClient());

        Assert.Equal("faiss", source.Name);
        Assert.Equal(CandidateSourceKind.Personalized, source.Kind);
    }

    [Fact]
    public async Task MapsIdsAndScoresInOrder()
    {
        var client = new FakeFaissClient();
        client.Seed("u1", "a", "b", "c");

        var batch = await new FaissCandidateSource(client).FetchAsync(Request(), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ok, batch.Outcome);
        Assert.Equal(["a", "b", "c"], batch.Items.Select(i => i.ArticleId));
        Assert.Equal([0, 1, 2], batch.Items.Select(i => i.Rank));
        Assert.Equal(1d, batch.Items[0].Score);
        Assert.True(batch.Items[0].Score > batch.Items[1].Score);
        Assert.All(batch.Items, item => Assert.Equal("faiss", item.Source));
    }

    [Fact]
    public async Task AsksForTheWholeCandidatePool()
    {
        var client = new FakeFaissClient();
        client.Seed("u1", "a");

        await new FaissCandidateSource(client).FetchAsync(Request(poolSize: 150), CancellationToken.None);

        Assert.Equal(150, client.LastTopK);
    }

    [Fact]
    public async Task MissingUserEmbeddingIsDisabledNotAFailure()
    {
        // 204 from the service: the user has no embedding yet. CandidateFeedStrategy does not count
        // Disabled as degradation, so the feed must not be marked partial because of it.
        var batch = await new FaissCandidateSource(new FakeFaissClient())
            .FetchAsync(Request("cold-start"), CancellationToken.None);

        Assert.Equal(SourceOutcome.Disabled, batch.Outcome);
        Assert.Empty(batch.Items);
        Assert.Equal("no user embedding", batch.Error);
    }

    [Fact]
    public async Task EmptyHitListIsEmptyNotDisabled()
    {
        var client = new FakeFaissClient();
        client.Seed("u1");

        var batch = await new FaissCandidateSource(client).FetchAsync(Request(), CancellationToken.None);

        Assert.Equal(SourceOutcome.Empty, batch.Outcome);
    }

    [Fact]
    public async Task FailuresBubbleUpForSourceExecutorToClassify()
    {
        var client = new FakeFaissClient { Faulted = true };
        client.Seed("u1", "a");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => new FaissCandidateSource(client).FetchAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task SlowSearchIsCutByTheSourceDeadline()
    {
        var client = new FakeFaissClient { ArtificialDelay = TimeSpan.FromMilliseconds(400) };
        client.Seed("u1", "a");
        var source = new FaissCandidateSource(client);

        var (batch, outcome) = await TestFactories.Executor().RunAsync(
            source.Name,
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None,
            ct => source.FetchAsync(Request(), ct));

        Assert.Equal(SourceOutcome.Timeout, outcome);
        Assert.Null(batch);
    }
}
