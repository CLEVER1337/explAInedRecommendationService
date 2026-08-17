using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public sealed class FaissHttpClient : IFaissClient
{
    private readonly HttpClient _http;

    public FaissHttpClient(HttpClient http) => _http = http;

    public async Task<FaissSearchResult?> SearchAsync(string userId, int topK, CancellationToken ct)
    {
        // The user vector stays on the Python side: only the id and top_k cross the boundary.
        using var response = await _http.PostAsJsonAsync("search", new SearchRequest(userId, topK), ct);

        // 204 = this user has no embedding yet. Not an error, not an empty result set.
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"faiss service returned {(int)response.StatusCode} for search");
        }

        var body = await response.Content.ReadFromJsonAsync<SearchResponse>(ct);

        return new FaissSearchResult(body?.Ids ?? [], body?.Scores ?? []);
    }

    private sealed record SearchRequest(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("top_k")] int TopK);

    private sealed record SearchResponse(
        [property: JsonPropertyName("ids")] List<string>? Ids,
        [property: JsonPropertyName("scores")] List<double>? Scores);
}
