using System.Net.Http.Json;

public sealed class ArticleHttpClient : IArticleClient
{
    private readonly HttpClient _http;

    public ArticleHttpClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ArticleSummary>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return Array.Empty<ArticleSummary>();

        var query = Uri.EscapeDataString(string.Join(',', ids));

        return await GetAsync($"articles/batch?ids={query}", ct);
    }

    public Task<IReadOnlyList<ArticleSummary>> GetRecentAsync(int limit, int offset, CancellationToken ct) =>
        GetAsync($"articles/recent?limit={limit}&offset={offset}", ct);

    private async Task<IReadOnlyList<ArticleSummary>> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"article service returned {(int)response.StatusCode} for {path}");
        }

        var articles = await response.Content.ReadFromJsonAsync<List<ArticleSummary>>(ct);

        return articles ?? [];
    }
}
