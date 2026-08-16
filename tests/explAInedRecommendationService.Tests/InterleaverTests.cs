public class InterleaverTests
{
    private static IReadOnlyList<Candidate> List(string source, params string[] ids) =>
        ids.Select((id, index) => new Candidate(id, ids.Length - index, source, index)).ToList();

    [Fact]
    public void RoundRobin_AlternatesSources_AndKeepsWithinSourceOrder()
    {
        var a = List("als", "a1", "a2", "a3");
        var b = List("faiss", "b1", "b2");

        var merged = Interleaver.RoundRobin([a, b], new HashSet<string>(), take: 10);

        Assert.Equal(new[] { "a1", "b1", "a2", "b2", "a3" }, merged.Select(c => c.ArticleId));
    }

    [Fact]
    public void RoundRobin_IsDeterministic()
    {
        var lists = new[] { List("als", "a1", "a2"), List("trending", "t1", "t2", "t3") };

        var first = Interleaver.RoundRobin(lists, new HashSet<string>(), take: 5);
        var second = Interleaver.RoundRobin(lists, new HashSet<string>(), take: 5);

        Assert.Equal(first.Select(c => c.ArticleId), second.Select(c => c.ArticleId));
    }

    [Fact]
    public void RoundRobin_DedupesAcrossSources()
    {
        var a = List("als", "x", "y");
        var b = List("trending", "x", "z");

        var merged = Interleaver.RoundRobin([a, b], new HashSet<string>(), take: 10);

        Assert.Equal(new[] { "x", "z", "y" }, merged.Select(c => c.ArticleId));
    }

    [Fact]
    public void RoundRobin_SkipsExcluded()
    {
        var a = List("als", "seen", "fresh");

        var merged = Interleaver.RoundRobin([a], new HashSet<string> { "seen" }, take: 10);

        Assert.Equal(new[] { "fresh" }, merged.Select(c => c.ArticleId));
    }

    [Fact]
    public void RoundRobin_HonoursTake_AndToleratesEmptyLists()
    {
        var merged = Interleaver.RoundRobin(
            [Array.Empty<Candidate>(), List("trending", "t1", "t2", "t3")],
            new HashSet<string>(),
            take: 2);

        Assert.Equal(new[] { "t1", "t2" }, merged.Select(c => c.ArticleId));
    }

    [Fact]
    public void RoundRobin_ReturnsEmpty_WhenNothingSurvives()
    {
        var merged = Interleaver.RoundRobin([List("als", "a")], new HashSet<string> { "a" }, take: 5);

        Assert.Empty(merged);
    }
}
