using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.SafetyZones.Domain;

public sealed class RoadClosure
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RoadName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ClosureSeverity Severity { get; set; } = ClosureSeverity.TotalClosure;
    public string CoordinatesJson { get; set; } = "[]";
    public string BlockedReason { get; set; } = string.Empty;
    public string? AlternateRouteAdvice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ReportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EstimatedReopenUtc { get; set; }
}
