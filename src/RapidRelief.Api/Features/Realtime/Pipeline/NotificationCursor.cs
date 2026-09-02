using System.Globalization;
using System.Text;

namespace RapidRelief.Api.Features.Realtime.Pipeline;

/// <summary>D-038 opaque keyset cursor: base64url("{CreatedAtUtc.UtcTicks}:{Id:D}").</summary>
public static class NotificationCursor
{
    public static string Encode(DateTimeOffset createdAtUtc, Guid id)
    {
        var raw = Encoding.UTF8.GetBytes(
            $"{createdAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id:D}");
        return Convert.ToBase64String(raw).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static bool TryDecode(string? value, out DateTimeOffset createdAtUtc, out Guid id)
    {
        createdAtUtc = default;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        if (!long.TryParse(decoded[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            ticks > DateTimeOffset.MaxValue.UtcTicks ||
            !Guid.TryParseExact(decoded[(separator + 1)..], "D", out var parsedId))
        {
            return false;
        }

        createdAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
        id = parsedId;
        return true;
    }
}
