using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

[Route("api/feed")]
public class FeedController : Controller
{
    private readonly FeedOrchestrator _orchestrator;
    private readonly FeedOptions _options;
    private readonly ILogger<FeedController> _logger;

    public FeedController(FeedOrchestrator orchestrator, IOptions<FeedOptions> options, ILogger<FeedController> logger)
    {
        _orchestrator = orchestrator;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    public async Task<IResult> GetFeed([FromQuery] int? limit, [FromQuery] string? cursor, CancellationToken ct)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var effectiveLimit = Math.Clamp(limit ?? _options.DefaultLimit, 1, _options.MaxLimit);
        if (limit is null or <= 0) effectiveLimit = _options.DefaultLimit;

        FeedResult result;

        try
        {
            result = await _orchestrator.GetFeedAsync(userId, effectiveLimit, cursor, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feed pipeline failed outright for user {UserId}", userId);

            result = new FeedResult(
                Array.Empty<FeedItemDto>(), FeedLevel.Empty, Guid.NewGuid().ToString("N"),
                null, "feed pipeline failed", Rebuilt: false, Array.Empty<SourceDiagnosticDto>());
        }

        Response.Headers["X-Feed-Source"] = result.Level.ToWire();
        Response.Headers["X-Feed-Id"] = result.FeedId;
        if (result.Rebuilt) Response.Headers["X-Feed-Rebuilt"] = "1";

        return Results.Ok(new FeedResponseDto(
            result.Items,
            result.Level.ToWire(),
            result.FeedId,
            result.NextCursor,
            result.Reason,
            result.Degraded,
            result.Sources));
    }
}
