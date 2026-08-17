using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public class FaissFeedEndpointTests : IClassFixture<RecommendationWebApplicationFactory>
{
    private const string JwtKey = "JwtSecretPlaceHolder_explAIned32";
    private const string Issuer = "http://localhost:5125/";
    private const string Audience = "http://localhost:5125/";
    private const string UserId = "faiss-user";

    private readonly RecommendationWebApplicationFactory _factory;

    public FaissFeedEndpointTests(RecommendationWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ArticleClient.Clear();
        _factory.FaissClient.Clear();
        _factory.Snapshots.Clear();
        _factory.Store.Faulted = false;
        _factory.Store.SeedTrending();
        _factory.Store.SeedViewed(UserId);
    }

    [Fact]
    public async Task SeededFaiss_ServesUnrankedPersonalizedCandidates()
    {
        _factory.ArticleClient.SeedMany("f1", "f2", "f3");
        _factory.FaissClient.Seed(UserId, "f1", "f2", "f3");

        var response = await CreateClient().GetAsync("/api/feed?limit=3");
        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unranked", LevelOf(response));
        Assert.Equal("unranked", body!.Level);
        Assert.True(body.Degraded);
        Assert.Equal(["f1", "f2", "f3"], body.Items.Select(item => item.Id));
        Assert.Contains(body.Sources, source => source is { Name: "faiss", Outcome: "ok" });
    }

    [Fact]
    public async Task UserWithoutEmbedding_FallsBackToTrending_AndFaissReportsDisabled()
    {
        _factory.ArticleClient.SeedMany("t1", "t2", "t3");
        _factory.Store.SeedTrending("t1", "t2", "t3");

        var response = await CreateClient().GetAsync("/api/feed?limit=3");
        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("trending", LevelOf(response));
        Assert.Contains(body!.Sources, source => source is { Name: "faiss", Outcome: "disabled" });
    }

    [Fact]
    public async Task FaissDown_DegradesToTrending_WithoutFailingTheRequest()
    {
        _factory.ArticleClient.SeedMany("t1", "t2", "t3");
        _factory.Store.SeedTrending("t1", "t2", "t3");
        _factory.FaissClient.Seed(UserId, "f1", "f2");
        _factory.FaissClient.Faulted = true;

        var response = await CreateClient().GetAsync("/api/feed?limit=3");
        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("trending", LevelOf(response));
        Assert.Contains(body!.Sources, source => source is { Name: "faiss", Outcome: "faulted" });
    }

    [Fact]
    public async Task ArticlesMissingFromTheArticleService_AreSimplyDropped()
    {
        // A candidate archived between search and hydration must not break the page.
        _factory.ArticleClient.SeedMany("f1");
        _factory.FaissClient.Seed(UserId, "f1", "gone");

        var body = await CreateClient()
            .GetFromJsonAsync<FeedResponseDto>("/api/feed?limit=5");

        Assert.Equal(["f1"], body!.Items.Select(item => item.Id));
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(UserId));

        return client;
    }

    private static string CreateAccessToken(string userId)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Typ, "access"),
            },
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string LevelOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Feed-Source", out var values) ? values.First() : "<missing>";
}
