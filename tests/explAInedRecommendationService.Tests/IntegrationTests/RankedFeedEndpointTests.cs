using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;

public class RankedFeedEndpointTests : IClassFixture<RankedFeedEndpointTests.RankedFactory>
{
    private const string JwtKey = "JwtSecretPlaceHolder_explAIned32";
    private const string Issuer = "http://localhost:5125/";
    private const string Audience = "http://localhost:5125/";
    private const string UserId = "ranked-user";

    public sealed class RankedFactory : RecommendationWebApplicationFactory
    {
        public StubRanker Stub { get; } = new();

        public RankedFactory() => Ranker = Stub;
    }

    private readonly RankedFactory _factory;

    public RankedFeedEndpointTests(RankedFactory factory)
    {
        _factory = factory;
        _factory.ArticleClient.Clear();
        _factory.FaissClient.Clear();
        _factory.Snapshots.Clear();
        _factory.Store.Faulted = false;
        _factory.Store.SeedTrending();
        _factory.Store.SeedViewed(UserId);
        _factory.Stub.Succeeds = true;
    }

    [Fact]
    public async Task EverythingAnswering_ServesPersonalized()
    {
        _factory.ArticleClient.SeedMany("r1", "r2", "r3");
        _factory.FaissClient.Seed(UserId, "r1", "r2", "r3");

        var response = await CreateClient().GetAsync("/api/feed?limit=3");
        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("personalized", LevelOf(response));
        Assert.False(body!.Degraded);
    }

    [Fact]
    public async Task TheRankerDecidesTheOrder()
    {
        _factory.ArticleClient.SeedMany("r1", "r2", "r3");
        _factory.FaissClient.Seed(UserId, "r1", "r2", "r3");

        var body = await CreateClient().GetFromJsonAsync<FeedResponseDto>("/api/feed?limit=3");

        Assert.Equal(["r3", "r2", "r1"], body!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task ARankerThatFails_DropsToUnrankedWithTheSameCandidates()
    {
        _factory.ArticleClient.SeedMany("r1", "r2", "r3");
        _factory.FaissClient.Seed(UserId, "r1", "r2", "r3");
        _factory.Stub.Succeeds = false;

        var response = await CreateClient().GetAsync("/api/feed?limit=3");
        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unranked", body!.Level);
        Assert.Equal(3, body.Items.Count);
    }

    [Fact]
    public async Task ADeadSource_MakesThePagePartialRatherThanPersonalized()
    {
        _factory.ArticleClient.SeedMany("a1", "a2", "a3");
        _factory.Store.SeedAlsCandidates(UserId, "a1", "a2", "a3");
        _factory.FaissClient.Faulted = true;
        _factory.FaissClient.Seed(UserId, "ignored");

        var response = await CreateClient().GetAsync("/api/feed?limit=3");
        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("partial", body!.Level);
        Assert.Contains(body.Sources, source => source is { Name: "als", Outcome: "ok" });
        Assert.Contains(body.Sources, source => source is { Name: "faiss", Outcome: "faulted" });
        Assert.False(body.Degraded);
    }

    [Fact]
    public async Task AlsAloneIsEnoughToReachPersonalized()
    {
        _factory.ArticleClient.SeedMany("a1", "a2");
        _factory.Store.SeedAlsCandidates(UserId, "a1", "a2");

        var response = await CreateClient().GetAsync("/api/feed?limit=2");
        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal("personalized", body!.Level);
        Assert.Contains(body.Sources, source => source is { Name: "faiss", Outcome: "disabled" });
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());

        return client;
    }

    private static string? LevelOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Feed-Source", out var values) ? values.FirstOrDefault() : null;

    private static string Token()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, UserId)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
