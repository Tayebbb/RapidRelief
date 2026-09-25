using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record SafetyZoneDto(
    Guid Id,
    string Name,
    string Description,
    ZoneType ZoneType,
    Severity Severity,
    string GeometryType,
    double CenterLat,
    double CenterLng,
    double RadiusMeters,
    string CoordinatesJson,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);
