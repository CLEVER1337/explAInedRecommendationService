public class FeedCursorTests
{
    [Fact]
    public void Encode_Decode_RoundTrips()
    {
        var cursor = FeedCursor.Create("abc123", FeedLevel.Trending, 40);

        Assert.True(FeedCursor.TryDecode(FeedCursor.Encode(cursor), out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(cursor.FeedId, decoded!.FeedId);
        Assert.Equal(FeedLevel.Trending, decoded.Level);
        Assert.Equal(40, decoded.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("YWJj")]
    public void TryDecode_ReturnsFalse_OnGarbage_WithoutThrowing(string? raw)
    {
        Assert.False(FeedCursor.TryDecode(raw, out var cursor));
        Assert.Null(cursor);
    }

    [Fact]
    public void TryDecode_ReturnsFalse_OnTamperedPayload()
    {
        var encoded = FeedCursor.Encode(FeedCursor.Create("abc", FeedLevel.Recent, 20));
        var tampered = encoded[..^3] + "AAA";

        var exception = Record.Exception(() => FeedCursor.TryDecode(tampered, out _));

        Assert.Null(exception);
    }

    [Fact]
    public void TryDecode_RejectsUnknownVersion()
    {
        var future = new FeedCursor(99, "abc", FeedLevel.Trending, 0, 0);

        Assert.False(FeedCursor.TryDecode(FeedCursor.Encode(future), out _));
    }

    [Fact]
    public void TryDecode_RejectsNegativeOffset()
    {
        var broken = new FeedCursor(FeedCursor.CurrentVersion, "abc", FeedLevel.Trending, -1, 0);

        Assert.False(FeedCursor.TryDecode(FeedCursor.Encode(broken), out _));
    }
}
