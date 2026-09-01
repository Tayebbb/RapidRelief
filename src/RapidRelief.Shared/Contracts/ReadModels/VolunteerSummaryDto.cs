using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record VolunteerSummaryDto(Guid Id, string Name, IReadOnlyList<string> Skills,
    bool IsAvailable, GeoPoint? Location);
