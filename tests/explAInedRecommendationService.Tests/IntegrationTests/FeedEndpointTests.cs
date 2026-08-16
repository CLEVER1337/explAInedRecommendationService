using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public class FeedEndpointTests : IClassFixture<RecommendationWebApplicationFactory>
{
    private const string JwtKey = "JwtSecretPlaceHolder_explAIned32";
    private const string Issuer = "http://localhost:5125/";
    private const string Audience = "http://localhost:5125/";
    private const string UserId = "user-1";

    private readonly RecommendationWebApplicationFactory _factory;

    public FeedEndpointTests(RecommendationWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ArticleClient.Clear();
        _factory.Snapshots.Clear();
        _factory.Store.Faulted = false;
        _factory.Store.SeedTrending();
        _factory.Store.SeedViewed(UserId);
    }

    private HttpClient CreateClient(string? userId = UserId)
    {
        var client = _factory.CreateClient();

        if (userId is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateAccessToken(userId));
        }

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
                new Claim(JwtRegisteredClaimNames.Email, $"{userId}@test"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Typ, "access"),
            },
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string LevelOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Feed-Source", out var values) ? values.First() : "<missing>";

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var client = CreateClient(userId: null);

        var response = await client.GetAsync("/api/feed");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GarbageToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");

        var response = await client.GetAsync("/api/feed");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SeededTrending_ServesTrendingLevel()
    {
        _factory.Store.SeedTrending("a", "b");
        _factory.ArticleClient.SeedMany("a", "b");

        var response = await CreateClient().GetAsync("/api/feed?limit=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("trending", LevelOf(response));

        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Items.Count);
        Assert.True(body.Degraded);
        Assert.False(string.IsNullOrWhiteSpace(body.FeedId));
    }

    [Fact]
    public async Task StoreDown_ServesRecentLevel()
    {
        _factory.ArticleClient.SeedMany("r1", "r2");
        _factory.Store.Faulted = true;

        var response = await CreateClient().GetAsync("/api/feed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("recent", LevelOf(response));
    }

    [Fact]
    public async Task EverythingDownWithColdSnapshot_ServesEmptyWithReason()
    {
        _factory.Store.Faulted = true;
        _factory.ArticleClient.Faulted = true;

        var response = await CreateClient("cold-start-user").GetAsync("/api/feed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("empty", LevelOf(response));

        var body = await response.Content.ReadFromJsonAsync<FeedResponseDto>();
        Assert.NotNull(body);
        Assert.Empty(body!.Items);
        Assert.False(string.IsNullOrWhiteSpace(body.Reason));
    }

    [Fact]
    public async Task SecondPage_KeepsLevel_AndReturnsDifferentItems()
    {
        _factory.Store.SeedTrending("a", "b", "c", "d");
        _factory.ArticleClient.SeedMany("a", "b", "c", "d");
        var client = CreateClient();

        var firstResponse = await client.GetAsync("/api/feed?limit=2");
        var first = await firstResponse.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.NotNull(first?.NextCursor);

        var secondResponse = await client.GetAsync($"/api/feed?limit=2&cursor={Uri.EscapeDataString(first!.NextCursor!)}");
        var second = await secondResponse.Content.ReadFromJsonAsync<FeedResponseDto>();

        Assert.Equal(LevelOf(firstResponse), LevelOf(secondResponse));
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second!.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task TamperedCursor_Returns200()
    {
        _factory.Store.SeedTrending("a");
        _factory.ArticleClient.SeedMany("a");

        var response = await CreateClient().GetAsync("/api/feed?cursor=%21%21broken%21%21");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NeverReturns5xx(bool storeDown, bool articlesDown)
    {
        _factory.Store.SeedTrending("a", "b");
        _factory.ArticleClient.SeedMany("a", "b");
        _factory.Store.Faulted = storeDown;
        _factory.ArticleClient.Faulted = articlesDown;

        var response = await CreateClient().GetAsync("/api/feed?limit=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("<missing>", LevelOf(response));
    }

    [Fact]
    public async Task Metrics_ExposeFeedCounters()
    {
        _factory.Store.SeedTrending("a");
        _factory.ArticleClient.SeedMany("a");
        await CreateClient().GetAsync("/api/feed");

        var metrics = await _factory.CreateClient().GetStringAsync("/metrics");

        Assert.Contains("feed_requests_total", metrics);
    }
}
