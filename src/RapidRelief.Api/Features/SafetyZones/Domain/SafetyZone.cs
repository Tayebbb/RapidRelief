using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.SafetyZones.Domain;

public sealed class SafetyZone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ZoneType ZoneType { get; set; } = ZoneType.DangerZone;
    public Severity Severity { get; set; } = Severity.Moderate;
    public string GeometryType { get; set; } = "Circle"; // "Circle" or "Polygon"
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double RadiusMeters { get; set; }
    public string CoordinatesJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
