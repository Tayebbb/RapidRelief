using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Features.Registry.Domain;

public sealed class Volunteer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public List<string> Skills { get; set; } = [];
    public string Status { get; set; } = "Available"; // "Available", "Assigned", "Unavailable"
    public string ContactNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public GeoPoint? Location => Latitude.HasValue && Longitude.HasValue ? new GeoPoint(Latitude.Value, Longitude.Value) : null;
}
