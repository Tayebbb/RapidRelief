using System.Globalization;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Ai;

/// <summary>Everything the priority engine is allowed to reason about. Pure data, no services.</summary>
internal sealed record PriorityInputs(
    Severity Severity,
    bool IsSos,
    int AffectedPeopleCount,
    bool MedicalUrgency,
    DateTimeOffset ReportedAtUtc,
    DateTimeOffset NowUtc,
    double Confidence,
    int NearbyOpenIncidents,
    ResponderAvailabilityDto Responders);

/// <summary>The score plus the arithmetic that produced it — never one without the other.</summary>
internal sealed record PriorityResult(
    double Score,
    string Band,
    string Urgency,
    IReadOnlyList<AiPriorityFactorDto> Factors,
    string Explanation);

/// <summary>
/// Explainable priority model. Every factor contributes named points with the evidence that
/// earned them, so "why is this Critical?" is answered from the data rather than from prose the
/// model invented. Deterministic: the same inputs always produce the same score and wording.
/// </summary>
internal static class IncidentPriorityEngine
{
    // Weights sum to 100 at their individual maxima; the total is clamped, so a genuinely
    // catastrophic SOS saturates rather than needing every factor to fire.
    private const double SeverityWeight = 12;      // 12..60
    private const double SosPoints = 20;
    private const double MaxPeoplePoints = 10;
    private const double MedicalPoints = 12;
    private const double MaxWaitPoints = 14;
    private const double MaxClusterPoints = 8;
    private const double MaxScarcityPoints = 8;

    /// <summary>Waiting time stops adding urgency after this — beyond it the case is simply late.</summary>
    private static readonly TimeSpan WaitSaturation = TimeSpan.FromHours(6);

    public static PriorityResult Compute(PriorityInputs inputs)
    {
        var factors = new List<AiPriorityFactorDto>();

        // Confidence only damps the model's own severity claim; declared SOS and head counts
        // are facts from the reporter and are never discounted by model uncertainty.
        var confidence = Math.Clamp(inputs.Confidence, 0, 1);
        var severityPoints = Math.Round((int)inputs.Severity * SeverityWeight * Damping(confidence), 1);
        factors.Add(new AiPriorityFactorDto("severity", "Assessed severity", severityPoints,
            confidence < 0.999
                ? $"{inputs.Severity} ({(int)inputs.Severity}/5) at {confidence:P0} confidence"
                : $"{inputs.Severity} ({(int)inputs.Severity}/5)"));

        if (inputs.IsSos)
        {
            factors.Add(new AiPriorityFactorDto("sos", "SOS raised", SosPoints,
                "The reporter pressed the emergency button"));
        }

        if (inputs.AffectedPeopleCount > 0)
        {
            // Logarithmic: the step from 1 to 10 people matters far more than 200 to 210.
            var points = Math.Round(Math.Min(MaxPeoplePoints,
                MaxPeoplePoints * Math.Log10(1 + inputs.AffectedPeopleCount) / Math.Log10(51)), 1);
            factors.Add(new AiPriorityFactorDto("people", "People affected", points,
                $"{inputs.AffectedPeopleCount} reported affected"));
        }

        if (inputs.MedicalUrgency)
        {
            factors.Add(new AiPriorityFactorDto("medical", "Medical urgency", MedicalPoints,
                "Injuries, entrapment or a medical emergency described"));
        }

        var waited = inputs.NowUtc - inputs.ReportedAtUtc;
        if (waited > TimeSpan.Zero)
        {
            var points = Math.Round(MaxWaitPoints * Math.Min(1, waited.TotalHours / WaitSaturation.TotalHours), 1);
            if (points > 0)
            {
                factors.Add(new AiPriorityFactorDto("waiting", "Waiting time", points,
                    $"Reported {Humanise(waited)} ago and still open"));
            }
        }

        if (inputs.NearbyOpenIncidents > 0)
        {
            var points = Math.Round(Math.Min(MaxClusterPoints, inputs.NearbyOpenIncidents * 2.0), 1);
            factors.Add(new AiPriorityFactorDto("location", "Location risk", points,
                $"{inputs.NearbyOpenIncidents} other open incident{(inputs.NearbyOpenIncidents == 1 ? "" : "s")} within 2 km"));
        }

        var scarcity = Scarcity(inputs.Responders);
        if (scarcity is { } scarcityFactor)
        {
            factors.Add(scarcityFactor);
        }

        var score = Math.Clamp(factors.Sum(f => f.Points), 0, 100);
        var band = Band(score, inputs.IsSos, inputs.Severity);
        var urgency = Urgency(band);

        return new PriorityResult(Math.Round(score, 1), band, urgency, factors, Explain(band, score, factors));
    }

    /// <summary>Confidence 1.0 keeps the full weight; 0.0 keeps 70% — never zero, an unsure read is still a read.</summary>
    private static double Damping(double confidence) => 0.7 + (0.3 * confidence);

    private static AiPriorityFactorDto? Scarcity(ResponderAvailabilityDto responders)
    {
        if (responders.TotalTeams == 0)
        {
            // No registry at all is an unknown, not a shortage — inventing urgency here would
            // silently inflate every score on a fresh deployment.
            return null;
        }

        if (responders.AvailableTeams == 0)
        {
            return new AiPriorityFactorDto("resources", "No team free", MaxScarcityPoints,
                $"All {responders.TotalTeams} teams are committed ({responders.OpenMissions} open missions)");
        }

        var free = responders.AvailableTeams / (double)responders.TotalTeams;
        if (free > 0.25)
        {
            return null;
        }

        var points = Math.Round(MaxScarcityPoints * (1 - (free / 0.25)), 1);
        return points <= 0
            ? null
            : new AiPriorityFactorDto("resources", "Rescue capacity stretched", points,
                $"Only {responders.AvailableTeams} of {responders.TotalTeams} teams free");
    }

    /// <summary>
    /// An SOS or a catastrophic assessment floors the band at Critical: a low arithmetic total
    /// must never let a life-threatening call read as routine.
    /// </summary>
    private static string Band(double score, bool isSos, Severity severity)
    {
        if (isSos || severity == Severity.Catastrophic)
        {
            return score >= 60 ? "Critical" : "High";
        }

        return score switch
        {
            >= 75 => "Critical",
            >= 55 => "High",
            >= 35 => "Medium",
            _ => "Low",
        };
    }

    private static string Urgency(string band) => band switch
    {
        "Critical" => AiUrgency.Immediate,
        "High" => AiUrgency.Urgent,
        "Medium" => AiUrgency.Standard,
        _ => AiUrgency.Monitor,
    };

    private static string Explain(string band, double score, IReadOnlyList<AiPriorityFactorDto> factors)
    {
        var top = factors
            .Where(f => f.Points > 0)
            .OrderByDescending(f => f.Points)
            .Take(4)
            .Select(f => f.Evidence.ToLowerInvariant())
            .ToList();

        return top.Count == 0
            ? string.Create(CultureInfo.InvariantCulture, $"Scored {score:F0}/100 ({band}) — no aggravating factors found.")
            : string.Create(CultureInfo.InvariantCulture,
                $"Scored {score:F0}/100 ({band}) because of {string.Join(" + ", top)}.");
    }

    private static string Humanise(TimeSpan span) => span switch
    {
        { TotalMinutes: < 1 } => "less than a minute",
        { TotalMinutes: < 60 } => $"{span.TotalMinutes:F0} minutes",
        { TotalHours: < 24 } => $"{span.TotalHours:F0} hours",
        _ => $"{span.TotalDays:F0} days",
    };
}
