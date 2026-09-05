using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// Structured analysis output, provider-independent. The rule-based analyser and the model path
/// both fill this shape, so a provider outage changes the confidence and the wording — never the
/// shape the rest of the system consumes.
/// </summary>
internal sealed record AiFindings(
    DisasterType PredictedType,
    Severity EstimatedSeverity,
    double Confidence,
    IReadOnlyList<string> DamageIndicators,
    int? EstimatedPeopleAffected,
    bool MedicalUrgency,
    string Summary,
    string Reasoning);
