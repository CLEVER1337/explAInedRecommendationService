using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json");

builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });

builder.Services.AddAuthorization();

builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection("Feed"));

builder.Services.AddHttpClient<IArticleClient, ArticleHttpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Articles:BaseUrl"] ?? "http://localhost:5036");
    client.Timeout = TimeSpan.FromSeconds(2);
});

var faissEnabled = builder.Configuration.GetValue("Faiss:Enabled", true);
if (faissEnabled)
{
    builder.Services.AddHttpClient<IFaissClient, FaissHttpClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Faiss:BaseUrl"] ?? "http://localhost:8001");
        client.Timeout = TimeSpan.FromSeconds(2);
    });
}

var rankingEnabled = builder.Configuration.GetValue("Ranking:Enabled", true);
if (rankingEnabled)
{
    builder.Services.AddHttpClient<IRanker, RankingHttpClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Ranking:BaseUrl"] ?? "http://localhost:8002");
        client.Timeout = TimeSpan.FromSeconds(2);
    });
}

var cacheProvider = builder.Configuration["Cache:Provider"] ?? "Redis";
if (string.Equals(cacheProvider, "Memory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<InMemoryRecommendationStore>();
    builder.Services.AddSingleton<IRecommendationStore>(sp => sp.GetRequiredService<InMemoryRecommendationStore>());
}
else
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        ConnectionMultiplexer.Connect(builder.Configuration["ConnectionStrings:Redis"]));
    builder.Services.AddSingleton<IRecommendationStore, RedisRecommendationStore>();
}

builder.Services.AddSingleton<SourceExecutor>();
builder.Services.AddSingleton<IFeedSnapshotStore, InMemoryFeedSnapshotStore>();

if (builder.Configuration.GetValue("ClickHouse:Enabled", true))
{
    builder.Services.AddHttpClient(ClickHouseImpressionLog.HttpClientName, client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["ClickHouse:BaseUrl"] ?? "http://localhost:8123");
        client.Timeout = TimeSpan.FromSeconds(5);
    });
    builder.Services.AddSingleton<ClickHouseImpressionLog>();
    builder.Services.AddSingleton<IImpressionLog>(sp => sp.GetRequiredService<ClickHouseImpressionLog>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ClickHouseImpressionLog>());
}
else
{
    builder.Services.AddSingleton<IImpressionLog, NoOpImpressionLog>();
}

if (!rankingEnabled)
{
    builder.Services.AddSingleton<IRanker, NullRanker>();
}

builder.Services.AddSingleton<TrendingCandidateSource>();
builder.Services.AddSingleton<ICandidateSource>(sp => sp.GetRequiredService<TrendingCandidateSource>());

if (faissEnabled)
{
    builder.Services.AddSingleton<FaissCandidateSource>();
    builder.Services.AddSingleton<ICandidateSource>(sp => sp.GetRequiredService<FaissCandidateSource>());
}

if (builder.Configuration.GetValue("Als:Enabled", true))
{
    builder.Services.AddSingleton<AlsCandidateSource>();
    builder.Services.AddSingleton<ICandidateSource>(sp => sp.GetRequiredService<AlsCandidateSource>());
}

builder.Services.AddSingleton<IFeedStrategy, CandidateFeedStrategy>();
builder.Services.AddSingleton<IFeedStrategy, TrendingFeedStrategy>();
builder.Services.AddSingleton<IFeedStrategy, RecentFeedStrategy>();
builder.Services.AddSingleton<IFeedStrategy, CachedSnapshotFeedStrategy>();
builder.Services.AddSingleton<IFeedStrategy, EmptyFeedStrategy>();

builder.Services.AddSingleton<FeedOrchestrator>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseHttpMetrics();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapMetrics();

app.Run();

public partial class Program;
