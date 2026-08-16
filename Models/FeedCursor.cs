using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record FeedCursor(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("fid")] string FeedId,
    [property: JsonPropertyName("lvl")] FeedLevel Level,
    [property: JsonPropertyName("off")] int Offset,
    [property: JsonPropertyName("iat")] long IssuedAt)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static FeedCursor Create(string feedId, FeedLevel level, int offset) =>
        new(CurrentVersion, feedId, level, offset, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public static string Encode(FeedCursor cursor)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(cursor, Options);
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? raw, out FeedCursor? cursor)
    {
        cursor = null;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            var padded = raw.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

            var decoded = JsonSerializer.Deserialize<FeedCursor>(Convert.FromBase64String(padded), Options);

            if (decoded is null || decoded.Version != CurrentVersion) return false;
            if (string.IsNullOrEmpty(decoded.FeedId) || decoded.Offset < 0) return false;
            if (!Enum.IsDefined(decoded.Level)) return false;

            cursor = decoded;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
