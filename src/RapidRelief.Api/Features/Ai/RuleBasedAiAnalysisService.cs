using System.Globalization;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// Permanent rule-based fallback (blueprint B4, rule §4.5/§4.8) — a pure deterministic function of
/// (request, now). Time comes from an injected <see cref="TimeProvider"/> so tests pin "now".
/// F8 adds the Gemini provider in this lane; this class never gets deleted.
/// </summary>
public sealed class RuleBasedAiAnalysisService : IAiAnalysisService
{
    private readonly TimeProvider _timeProvider;

    public RuleBasedAiAnalysisService(TimeProvider timeProvider) => _timeProvider = timeProvider;

    // Checked in fixed order — first match wins (deterministic).
    private static readonly (DisasterType Type, string[] Keywords)[] TypeKeywords =
    [
        (DisasterType.Fire, ["fire", "burning", "flames", "smoke"]),
        (DisasterType.BuildingCollapse, ["collapse", "collapsed", "rubble", "caved in"]),
        (DisasterType.Earthquake, ["earthquake", "tremor", "aftershock", "quake"]),
        (DisasterType.Flood, ["flood", "water rising", "submerged", "waterlogged", "under water"]),
    ];

    private static readonly string[] SeverityBumpWords = ["trapped", "children", "spreading", "injured"];

    // AiAnalysisRequest carries no reported severity (frozen contract), so the baseline derives
    // from the predicted type; bump words add one step, clamped at Catastrophic.
    private static Severity BaseSeverity(DisasterType type) => type switch
    {
        DisasterType.BuildingCollapse or DisasterType.Earthquake or DisasterType.Cyclone => Severity.Severe,
        DisasterType.Flood or DisasterType.Fire or DisasterType.Landslide => Severity.Moderate,
        _ => Severity.Minor,
    };

    public Task<AiAssessmentDto> AnalyzeIncidentAsync(AiAnalysisRequest request, CancellationToken ct = default)
    {
        var description = request.Description ?? string.Empty;

        var predictedType = request.ReportedType;
        foreach (var (type, keywords) in TypeKeywords)
        {
            if (keywords.Any(k => description.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                predictedType = type;
                break;
            }
        }

        var severity = BaseSeverity(predictedType);
        if (SeverityBumpWords.Any(w => description.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            severity = (Severity)Math.Min((int)severity + 1, (int)Severity.Catastrophic);
        }

        // F8: the shared formula — extracted verbatim so the Gemini path scores identically.
        var priority = PriorityFormula.Compute(severity, request.IsSos, request.ReportedAtUtc, _timeProvider.GetUtcNow());

        var summary = string.Create(CultureInfo.InvariantCulture,
            $"{predictedType} assessed at severity {(int)severity}/5{(request.IsSos ? " with SOS flag" : "")}; priority {priority:F0}/100.");

        return Task.FromResult(new AiAssessmentDto(
            request.IncidentId, predictedType, severity, priority, summary,
            PossibleDuplicateOfId: null, Provider: "RuleBased"));
    }
}
