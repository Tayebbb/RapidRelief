using System.Text;
using System.Text.RegularExpressions;

namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>Result of <see cref="AssistantSanitizer.Clean"/> (D-051).</summary>
internal readonly record struct SanitizedAnswer(string Text, bool Empty);

/// <summary>
/// D-051 answer sanitization contract, applied before persist and before response. No schema
/// mechanism exists for prose, so the charset/length/link contract is enforced imperatively.
/// Link AND phone-number stripping are deliberate: the assistant has no legitimate reason to
/// emit either, and a hallucinated one inside a trusted emergency UI is a phishing primitive.
/// Only 999 — short enough to fall under every digit rule — is meant to survive.
/// </summary>
internal static partial class AssistantSanitizer
{
    /// <summary>Only trust a boundary in the second half of the budget — never clamp to a stub.</summary>
    private const int MinBoundaryFraction = 2;

    public static SanitizedAnswer Clean(string? raw, int maxLength)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return new SanitizedAnswer(string.Empty, Empty: true);
        }

        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        var stripped = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (!char.IsControl(c) || c is '\n' or '\t')
            {
                stripped.Append(c);
            }
        }

        var text = LinkToken().Replace(stripped.ToString(), string.Empty);
        text = IpToken().Replace(text, string.Empty);
        text = DomainToken().Replace(text, string.Empty);
        text = PhoneToken().Replace(text, string.Empty);
        text = DigitRun().Replace(text, string.Empty);
        text = BlankLines().Replace(text, "\n\n");
        text = SpaceRuns().Replace(text, " ");
        text = string.Join('\n', text.Split('\n').Select(line => line.Trim(' '))).Trim();

        if (text.Length == 0)
        {
            return new SanitizedAnswer(string.Empty, Empty: true);
        }

        return new SanitizedAnswer(text.Length <= maxLength ? text : Clamp(text, maxLength), Empty: false);
    }

    private static string Clamp(string text, int maxLength)
    {
        var window = text[..maxLength];
        var newline = window.LastIndexOf('\n');
        var sentence = window.LastIndexOf(". ", StringComparison.Ordinal);
        var cut = Math.Max(newline, sentence >= 0 ? sentence + 1 : -1);
        return cut >= maxLength / MinBoundaryFraction ? text[..cut].TrimEnd() : window;
    }

    /// <summary>Anything carrying a scheme: http(s), www, and the script/data/dial families.</summary>
    [GeneratedRegex(
        @"(?i)\S*://\S*|\b(?:data|javascript|vbscript|file|blob|about|tel|sms|mailto|ftp|wss?):\S*|\bwww\.\S+",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkToken();

    /// <summary>Dotted-quad literals with any path — a scheme-less link is still a link.</summary>
    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{1,3}){3}\S*", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IpToken();

    // Bare "relief-help.org/apply" needs no scheme to be typed into a browser. The TLD list is
    // an allow-list on purpose: a generic \w+\.\w+ rule deletes prose like "shelter.Stay calm",
    // and silently eating safety instructions is the worse failure here. Two-letter TLDs that
    // are also English words (in, us, me, co) are excluded for the same reason.
    [GeneratedRegex(
        @"(?i)\b[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9-]+)*\.(?:com|net|org|edu|gov|mil|int|info|biz|io|app|dev|xyz|online|site|live|link|click|top|zip|ai|ly|cc|tv|bd|uk|pk|np|lk|de|fr|ru|cn|jp)\b\S*",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DomainToken();

    /// <summary>Grouped phone shapes ("0800-555-1212", "+880 1711 234567").</summary>
    [GeneratedRegex(@"\+?\d{2,5}[\s-]\d{3,5}[\s-]\d{3,6}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PhoneToken();

    /// <summary>Any 7+ digit run — long enough to be a phone number, never "999".</summary>
    [GeneratedRegex(@"\d{7,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"\n{3,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"[ ]{2,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SpaceRuns();
}
