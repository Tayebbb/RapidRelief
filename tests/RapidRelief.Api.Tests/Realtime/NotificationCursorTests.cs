using RapidRelief.Api.Features.Realtime.Pipeline;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>D-038 — the opaque keyset cursor must round-trip exactly and reject garbage.</summary>
public sealed class NotificationCursorTests
{
    private static readonly DateTimeOffset Anchor =
        new(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Encoded_cursor_round_trips_ticks_and_id()
    {
        var id = Guid.Parse("6c2d1f2e-6c3f-4d1a-9a2b-0f1e2d3c4b5a");
        var created = Anchor.AddTicks(1234567);

        var cursor = NotificationCursor.Encode(created, id);
        var decoded = NotificationCursor.TryDecode(cursor, out var createdOut, out var idOut);

        Assert.True(decoded);
        Assert.Equal(created.UtcTicks, createdOut.UtcTicks);
        Assert.Equal(TimeSpan.Zero, createdOut.Offset);
        Assert.Equal(id, idOut);
    }

    [Fact]
    public void Cursor_is_url_safe_base64_without_padding_or_reserved_characters()
    {
        var cursor = NotificationCursor.Encode(Anchor, Guid.NewGuid());

        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.DoesNotContain('=', cursor);
        Assert.Equal(Uri.EscapeDataString(cursor), cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("bm90LWEtY3Vyc29y")] // base64url of "not-a-cursor"
    public void Undecodable_values_are_rejected(string? value)
    {
        var decoded = NotificationCursor.TryDecode(value, out var createdOut, out var idOut);

        Assert.False(decoded);
        Assert.Equal(default, createdOut);
        Assert.Equal(Guid.Empty, idOut);
    }

    [Fact]
    public void Cursor_with_non_numeric_ticks_is_rejected()
    {
        var raw = Convert.ToBase64String("abc:6c2d1f2e-6c3f-4d1a-9a2b-0f1e2d3c4b5a"u8.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.False(NotificationCursor.TryDecode(raw, out _, out _));
    }

    [Fact]
    public void Cursor_with_out_of_range_ticks_is_rejected()
    {
        var raw = Convert.ToBase64String("99999999999999999999:6c2d1f2e-6c3f-4d1a-9a2b-0f1e2d3c4b5a"u8.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.False(NotificationCursor.TryDecode(raw, out _, out _));
    }
}
