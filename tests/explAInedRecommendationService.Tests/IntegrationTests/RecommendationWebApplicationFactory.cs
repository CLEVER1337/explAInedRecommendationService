using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class RecommendationWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeArticleClient ArticleClient { get; } = new();

    public InMemoryRecommendationStore Store => Services.GetRequiredService<InMemoryRecommendationStore>();

    public TestSnapshotStore Snapshots { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:Provider"] = "Memory",
                ["Articles:BaseUrl"] = "http://localhost",
                ["Jwt:Key"] = "JwtSecretPlaceHolder_explAIned32",
                ["Jwt:Issuer"] = "http://localhost:5125/",
                ["Jwt:Audience"] = "http://localhost:5125/",
            });
        });

        builder.ConfigureServices(services =>
        {
            Replace<IArticleClient>(services, ArticleClient);

            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            var store = new InMemoryRecommendationStore();
            services.AddSingleton(store);
            Replace<IRecommendationStore>(services, store);

            Replace<IFeedSnapshotStore>(services, Snapshots);
        });
    }

    private static void Replace<TService>(IServiceCollection services, object instance) where TService : class
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(TService)).ToList())
        {
            services.Remove(descriptor);
        }

        services.AddSingleton(typeof(TService), instance);
    }
}
