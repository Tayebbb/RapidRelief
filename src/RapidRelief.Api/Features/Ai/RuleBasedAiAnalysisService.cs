using System.Globalization;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// Permanent rule-based fallback (blueprint B4, rule §4.5/§4.8) — a pure deterministic function of
/// (request, now). Time comes from an injected <see cref="TimeProvider"/> so tests pin "now".
/// F8 adds the OpenRouter provider in this lane; this class never gets deleted.
/// </summary>
public sealed class RuleBasedAiAnalysisService : IAiAnalysisService
{
    /// <summary>Keyword matching is evidence, not inference — it never claims model-grade certainty.</summary>
    internal const double RuleBasedConfidence = 0.45;

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

    public Task<AiAssessmentDto> AnalyzeIncidentAsync(AiAnalysisRequest request, CancellationToken ct = default)
    {
        var findings = Analyze(request);
        var priority = PriorityFormula.Compute(findings.EstimatedSeverity, request.IsSos,
            request.ReportedAtUtc, _timeProvider.GetUtcNow());

        return Task.FromResult(new AiAssessmentDto(
            request.IncidentId, findings.PredictedType, findings.EstimatedSeverity, priority,
            findings.Summary, PossibleDuplicateOfId: null, Provider: "RuleBased"));
    }

    /// <summary>
    /// Structured findings from text alone. Used directly as the fallback and as the floor the
    /// model path is merged into, so an outage still yields damage indicators and a reason.
    /// </summary>
    internal AiFindings Analyze(AiAnalysisRequest request)
    {
        var description = request.Description ?? string.Empty;

        var predictedType = request.ReportedType;
        var typeEvidence = "the reporter's declared type";
        foreach (var (type, keywords) in TypeKeywords)
        {
            var hit = keywords.FirstOrDefault(k => description.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                predictedType = type;
                typeEvidence = $"the word \"{hit}\" in the report";
                break;
            }
        }

        var signals = IncidentSignalReader.Read(description);
        var severity = IncidentSignalReader.BaseSeverity(predictedType);
        if (signals.SeverityBump > 0)
        {
            severity = (Severity)Math.Min((int)severity + signals.SeverityBump, (int)Severity.Catastrophic);
        }

        var people = request.AffectedPeopleCount > 0 ? request.AffectedPeopleCount : signals.PeopleMentioned;

        var priority = PriorityFormula.Compute(severity, request.IsSos, request.ReportedAtUtc,
            _timeProvider.GetUtcNow());
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"{predictedType} assessed at severity {(int)severity}/5{(request.IsSos ? " with SOS flag" : "")}; priority {priority:F0}/100.");

        return new AiFindings(predictedType, severity, RuleBasedConfidence, signals.DamageIndicators,
            people, signals.MedicalUrgency, summary, Reason(predictedType, typeEvidence, severity, signals, people));
    }

    private static string Reason(
        DisasterType type, string typeEvidence, Severity severity, IncidentSignals signals, int? people)
    {
        var parts = new List<string> { $"Classified as {type} from {typeEvidence}" };

        parts.Add(signals.SeverityBump > 0
            ? $"severity raised to {severity} because the report describes an escalating situation"
            : $"severity {severity} is the baseline for {type} with nothing in the text raising it");

        if (signals.DamageIndicators.Count > 0)
        {
            parts.Add($"damage indicators found: {string.Join(", ", signals.DamageIndicators)}");
        }

        if (signals.MedicalUrgency)
        {
            parts.Add("medical wording present, so this is treated as a medical emergency");
        }

        if (people is { } count)
        {
            parts.Add($"{count} people affected");
        }

        parts.Add("no external model was used — this is keyword analysis of the report text");
        return string.Join("; ", parts) + ".";
    }
}
