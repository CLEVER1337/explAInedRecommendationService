using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public sealed class RankingHttpClient : IRanker
{
    public const string RankerName = "ranking-service";

    private readonly HttpClient _http;
    private readonly ILogger<RankingHttpClient> _logger;

    public RankingHttpClient(HttpClient http, ILogger<RankingHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string Name => RankerName;

    public async Task<RankResult> RankAsync(
        string userId, IReadOnlyList<Candidate> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return new RankResult(candidates, Succeeded: true);
        }

        var ids = candidates.Select(c => c.ArticleId).ToList();

        using var response = await _http.PostAsJsonAsync("rank", new RankRequest(userId, ids), ct);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return new RankResult(candidates, Succeeded: false, "ranker has no model");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ranking service returned {(int)response.StatusCode} for rank");
        }

        var body = await response.Content.ReadFromJsonAsync<RankResponse>(ct);
        var ranked = body?.Ranked;

        if (ranked is null || ranked.Count == 0)
        {
            return new RankResult(candidates, Succeeded: false, "ranker returned nothing");
        }

        var byId = candidates.ToDictionary(c => c.ArticleId, StringComparer.Ordinal);
        var ordered = new List<Candidate>(ranked.Count);

        foreach (var item in ranked)
        {
            if (item.Id is null || !byId.Remove(item.Id, out var candidate)) continue;

            ordered.Add(candidate with { Score = item.Score, Rank = ordered.Count });
        }

        if (byId.Count > 0)
        {
            _logger.LogWarning(
                "ranker returned {Returned} of {Sent} candidates; appending the remainder unranked",
                ordered.Count,
                candidates.Count);

            foreach (var leftover in candidates.Where(c => byId.ContainsKey(c.ArticleId)))
            {
                ordered.Add(leftover with { Rank = ordered.Count });
            }
        }

        return new RankResult(ordered, Succeeded: true);
    }

    private sealed record RankRequest(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("candidates")] IReadOnlyList<string> Candidates);

    private sealed record RankResponse(
        [property: JsonPropertyName("ranked")] List<RankedItem>? Ranked);

    private sealed record RankedItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("score")] double Score);
}
