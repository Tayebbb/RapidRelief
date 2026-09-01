using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Features.Shelters.Domain;

public sealed class Shelter
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public GeoPoint Location { get; set; } = new(0, 0);
    public int Capacity { get; set; }
    public int CurrentOccupancy { get; set; }
    public List<string> Facilities { get; set; } = new();
    public ShelterStatus Status { get; set; }
}

public enum ShelterStatus
{
    Open,
    Full,
    Closed
}
