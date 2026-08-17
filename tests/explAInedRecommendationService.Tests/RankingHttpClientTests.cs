using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class RankingHttpClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body = "")
        {
            _status = status;
            _body = body;
        }

        public string? LastRequestBody { get; private set; }

        public string? LastPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (RankingHttpClient Client, StubHandler Handler) Build(
        HttpStatusCode status, string body = "")
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8002/") };

        return (new RankingHttpClient(http, NullLogger<RankingHttpClient>.Instance), handler);
    }

    private static List<Candidate> Candidates(params string[] ids) =>
        ids.Select((id, index) => new Candidate(id, Score: 0, Source: "faiss", Rank: index)).ToList();

    [Fact]
    public async Task SendsIdsOnlyAsSnakeCaseJson()
    {
        var (client, handler) = Build(HttpStatusCode.OK, """{"ranked":[{"id":"a","score":1.0}]}""");

        await client.RankAsync("u1", Candidates("a"), CancellationToken.None);

        using var document = JsonDocument.Parse(handler.LastRequestBody!);

        Assert.Equal("/rank", handler.LastPath);
        Assert.Equal("u1", document.RootElement.GetProperty("user_id").GetString());
        Assert.Equal(["a"], document.RootElement.GetProperty("candidates")
            .EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ReordersCandidatesByTheReturnedOrder()
    {
        var (client, _) = Build(
            HttpStatusCode.OK,
            """{"ranked":[{"id":"c","score":0.9},{"id":"a","score":0.5},{"id":"b","score":0.1}]}""");

        var result = await client.RankAsync("u1", Candidates("a", "b", "c"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["c", "a", "b"], result.Items.Select(i => i.ArticleId));
        Assert.Equal([0, 1, 2], result.Items.Select(i => i.Rank));
        Assert.Equal(0.9, result.Items[0].Score);
    }

    [Fact]
    public async Task AnEmptyPoolSucceedsWithoutCallingTheService()
    {
        var (client, handler) = Build(HttpStatusCode.InternalServerError);

        var result = await client.RankAsync("u1", [], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(handler.LastRequestBody);
    }

    [Fact]
    public async Task NoModelYetIsAFailedRankNotAFailedRequest()
    {
        var (client, _) = Build(HttpStatusCode.ServiceUnavailable);
        var candidates = Candidates("a", "b");

        var result = await client.RankAsync("u1", candidates, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(["a", "b"], result.Items.Select(i => i.ArticleId));
        Assert.Equal("ranker has no model", result.Error);
    }

    [Fact]
    public async Task OtherErrorsThrowForSourceExecutorToClassify()
    {
        var (client, _) = Build(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.RankAsync("u1", Candidates("a"), CancellationToken.None));
    }

    [Fact]
    public async Task AnEmptyRankingKeepsTheOriginalPool()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"ranked":[]}""");

        var result = await client.RankAsync("u1", Candidates("a", "b"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task UnknownIdsFromTheServiceAreIgnored()
    {
        var (client, _) = Build(
            HttpStatusCode.OK,
            """{"ranked":[{"id":"ghost","score":9.0},{"id":"a","score":0.5}]}""");

        var result = await client.RankAsync("u1", Candidates("a"), CancellationToken.None);

        Assert.Equal(["a"], result.Items.Select(i => i.ArticleId));
    }

    [Fact]
    public async Task DroppedCandidatesAreAppendedRatherThanLost()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"ranked":[{"id":"b","score":0.9}]}""");

        var result = await client.RankAsync("u1", Candidates("a", "b", "c"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["b", "a", "c"], result.Items.Select(i => i.ArticleId));
        Assert.Equal([0, 1, 2], result.Items.Select(i => i.Rank));
    }

    [Fact]
    public async Task DuplicateIdsInTheResponseAreNotDuplicatedInTheResult()
    {
        var (client, _) = Build(
            HttpStatusCode.OK,
            """{"ranked":[{"id":"a","score":0.9},{"id":"a","score":0.8}]}""");

        var result = await client.RankAsync("u1", Candidates("a"), CancellationToken.None);

        Assert.Single(result.Items);
    }
}
