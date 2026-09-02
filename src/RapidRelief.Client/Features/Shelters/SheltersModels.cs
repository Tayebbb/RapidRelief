using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Features.Shelters;

public enum ShelterStatus { Open, Full, Closed }

public sealed record CreateShelterRequest(
    string Name,
    double Latitude,
    double Longitude,
    int Capacity,
    int CurrentOccupancy,
    List<string> Facilities,
    ShelterStatus Status);

public sealed record UpdateShelterRequest(
    string Name,
    double Latitude,
    double Longitude,
    int Capacity,
    int CurrentOccupancy,
    List<string> Facilities,
    ShelterStatus Status);

public sealed record UpdateOccupancyRequest(int CurrentOccupancy);

public sealed record ShelterDto(
    Guid Id,
    string Name,
    GeoPoint Location,
    int Capacity,
    int CurrentOccupancy,
    List<string> Facilities,
    ShelterStatus Status);
