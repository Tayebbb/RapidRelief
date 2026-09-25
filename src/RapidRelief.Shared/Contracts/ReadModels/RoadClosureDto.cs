using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record RoadClosureDto(
    Guid Id,
    string RoadName,
    string Description,
    ClosureSeverity Severity,
    string CoordinatesJson,
    string BlockedReason,
    string? AlternateRouteAdvice,
    bool IsActive,
    DateTimeOffset ReportedAtUtc,
    DateTimeOffset? EstimatedReopenUtc);
