using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record OverrideClassificationRequest(
    DisasterType DisasterType,
    Severity Severity,
    double? AdjustedPriorityScore,
    string Reason
);
