using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record HospitalSummaryDto(Guid Id, string Name, GeoPoint Location,
    int TotalBeds, int AvailableBeds, IReadOnlyList<string> Specialties);
