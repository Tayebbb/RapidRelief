using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

/// <summary>Provider: "RuleBased" | "FreeLlmPool".</summary>
public sealed record AiAssessmentDto(Guid IncidentId, DisasterType PredictedType, Severity EstimatedSeverity,
    double PriorityScore, string Summary, Guid? PossibleDuplicateOfId, string Provider);
