using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Features.Registry.Domain;

public sealed class Hospital
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int TotalBeds { get; set; }
    public int AvailableBeds { get; set; }
    public int IcuBeds { get; set; }
    public int AvailableIcuBeds { get; set; }
    public bool HasEmergency { get; set; } = true;
    public List<string> Specialties { get; set; } = [];
    public string ContactNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public GeoPoint Location => new(Latitude, Longitude);
}
