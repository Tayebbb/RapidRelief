using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Ai;

/// <summary>Deterministic evidence read straight out of the report text — no model involved.</summary>
internal sealed record IncidentSignals(
    IReadOnlyList<string> DamageIndicators,
    bool MedicalUrgency,
    int? PeopleMentioned,
    int SeverityBump);

/// <summary>
/// Keyword evidence extraction shared by the rule-based analyser, the priority engine inputs and
/// the duplicate detector. Every indicator carries the phrase that triggered it so the UI can show
/// why, and nothing here depends on an external service — this is the layer that keeps working
/// when the provider is down.
/// </summary>
internal static partial class IncidentSignalReader
{
    /// <summary>Ordered so the most operationally significant indicator is listed first.</summary>
    private static readonly (string Label, string[] Phrases)[] DamagePhrases =
    [
        ("People trapped", ["trapped", "stuck under", "buried", "pinned", "cannot get out", "can't get out"]),
        ("Injuries reported", ["injured", "injury", "bleeding", "wounded", "unconscious", "broken leg", "broken arm"]),
        ("Structural collapse", ["collapse", "collapsed", "rubble", "caved in", "cracked wall", "building fell"]),
        ("Fire spreading", ["spreading", "engulfed", "flames", "burning", "smoke filling"]),
        ("Rising water", ["water rising", "rising water", "submerged", "waist deep", "chest deep", "under water", "waterlogged"]),
        ("Access blocked", ["road blocked", "blocked road", "cannot reach", "no access", "bridge down", "impassable"]),
        ("Utilities down", ["no power", "power cut", "electric", "live wire", "gas leak", "no water supply"]),
        ("Vulnerable people present", ["children", "child", "elderly", "old man", "old woman", "pregnant", "disabled", "baby", "infant"]),
    ];

    private static readonly string[] MedicalPhrases =
    [
        "injured", "injury", "bleeding", "wounded", "unconscious", "not breathing", "cpr",
        "heart attack", "stroke", "seizure", "burns", "burnt", "fracture", "broken leg",
        "broken arm", "trapped", "pregnant", "labour", "labor pain", "ambulance", "medical",
    ];

    /// <summary>Phrases that raise the assessed severity one step above the type baseline.</summary>
    private static readonly string[] EscalationPhrases =
    [
        "trapped", "children", "spreading", "injured", "unconscious", "many people",
        "several families", "no access", "cannot reach", "rising fast", "gas leak",
    ];

    public static IncidentSignals Read(string? description)
    {
        var text = (description ?? string.Empty).ToLowerInvariant();
        if (text.Length == 0)
        {
            return new IncidentSignals([], false, null, 0);
        }

        var indicators = new List<string>();
        foreach (var (label, phrases) in DamagePhrases)
        {
            var hit = phrases.FirstOrDefault(p => text.Contains(p, StringComparison.Ordinal));
            if (hit is not null)
            {
                indicators.Add($"{label} (\"{hit}\")");
            }
        }

        var medical = MedicalPhrases.Any(p => text.Contains(p, StringComparison.Ordinal));
        var bump = EscalationPhrases.Any(p => text.Contains(p, StringComparison.Ordinal)) ? 1 : 0;

        return new IncidentSignals(indicators, medical, PeopleMentioned(text), bump);
    }

    /// <summary>
    /// Pulls a head count out of phrases like "4 people", "two children", "family of 6".
    /// Deliberately conservative: an unparseable count is null, never a guess.
    /// </summary>
    private static int? PeopleMentioned(string text)
    {
        var match = CountPhrase().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups["count"].Value;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric is > 0 and <= 10_000 ? numeric : null;
        }

        return raw switch
        {
            "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" => 10,
            _ => null,
        };
    }

    /// <summary>Lowercased alphanumeric word set with stop words removed — the similarity fingerprint.</summary>
    public static string Normalise(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(description.Length);
        foreach (var ch in description)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w, StringComparer.Ordinal);

        return string.Join(' ', words);
    }

    /// <summary>Jaccard overlap of the two normalised word sets, 0 when either side is empty.</summary>
    public static double Similarity(string normalisedA, string normalisedB)
    {
        if (normalisedA.Length == 0 || normalisedB.Length == 0)
        {
            return 0;
        }

        var a = normalisedA.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var b = normalisedB.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : Math.Round(intersection / (double)union, 3);
    }

    /// <summary>Baseline severity implied by the disaster type before any text evidence.</summary>
    public static Severity BaseSeverity(DisasterType type) => type switch
    {
        DisasterType.BuildingCollapse or DisasterType.Earthquake or DisasterType.Cyclone => Severity.Severe,
        DisasterType.Flood or DisasterType.Fire or DisasterType.Landslide => Severity.Moderate,
        _ => Severity.Minor,
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "with", "from", "this", "that", "there", "here", "have",
        "has", "was", "were", "been", "being", "our", "you", "your", "they", "them", "their",
        "please", "very", "some", "into", "out", "not", "all", "any", "can", "will", "near",
        "now", "get", "got", "need", "needs", "help",
    };

    [GeneratedRegex(@"(?<count>\d{1,5}|one|two|three|four|five|six|seven|eight|nine|ten)\s+(people|persons?|children|kids?|families|family|residents?|victims?|men|women|students?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountPhrase();
}
