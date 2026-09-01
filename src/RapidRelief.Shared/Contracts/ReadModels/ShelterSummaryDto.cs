using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record ShelterSummaryDto(Guid Id, string Name, GeoPoint Location,
    int Capacity, int Occupancy, bool IsOpen);
