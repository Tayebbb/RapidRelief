using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record IncidentSummaryDto(Guid Id, DisasterType Type, Severity Severity,
    IncidentStatus Status, GeoPoint Location, string Summary, DateTimeOffset ReportedAtUtc,
    bool IsSos, double? PriorityScore);
