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
builder.Services.AddSingleton<IImpressionLog, NoOpImpressionLog>();
builder.Services.AddSingleton<IRanker, NullRanker>();

builder.Services.AddSingleton<TrendingCandidateSource>();
builder.Services.AddSingleton<ICandidateSource>(sp => sp.GetRequiredService<TrendingCandidateSource>());

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
