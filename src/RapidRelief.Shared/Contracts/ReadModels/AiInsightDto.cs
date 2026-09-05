using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

/// <summary>One scored contribution to an incident's priority, with the evidence behind it.</summary>
public sealed record AiPriorityFactorDto(string Code, string Label, double Points, string Evidence);

/// <summary>
/// The full decision-support view of an incident: what the analyser concluded, how sure it is,
/// and exactly why the priority came out where it did. Never present these as confirmed facts —
/// see <see cref="Disclaimer"/>.
/// </summary>
public sealed record AiInsightDto(
    Guid IncidentId,
    DisasterType PredictedType,
    Severity EstimatedSeverity,
    double Confidence,
    string Urgency,
    int? EstimatedPeopleAffected,
    bool MedicalUrgency,
    IReadOnlyList<string> DamageIndicators,
    string Summary,
    string Reasoning,
    double PriorityScore,
    string PriorityBand,
    IReadOnlyList<AiPriorityFactorDto> PriorityFactors,
    string Provider,
    string? ModelName,
    Guid? PossibleDuplicateOfId,
    double? DuplicateConfidence,
    string? DuplicateReason,
    DateTimeOffset CreatedAtUtc)
{
    public const string Disclaimer =
        "AI-generated · decision support only. Verify against the report before acting.";

    /// <summary>Always true — clients must label the whole payload, not individual fields.</summary>
    public bool IsDecisionSupport => true;
}

/// <summary>Urgency bands, ordered most to least pressing.</summary>
public static class AiUrgency
{
    public const string Immediate = "Immediate";
    public const string Urgent = "Urgent";
    public const string Standard = "Standard";
    public const string Monitor = "Monitor";

    public static bool IsKnown(string value) =>
        value is Immediate or Urgent or Standard or Monitor;
}
