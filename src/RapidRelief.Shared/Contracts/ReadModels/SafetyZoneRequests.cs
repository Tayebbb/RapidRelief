using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record CreateSafetyZoneRequest(
    string Name,
    string Description,
    ZoneType ZoneType,
    Severity Severity,
    string GeometryType,
    double CenterLat,
    double CenterLng,
    double RadiusMeters,
    string? CoordinatesJson = null,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record UpdateSafetyZoneRequest(
    string Name,
    string Description,
    ZoneType ZoneType,
    Severity Severity,
    string GeometryType,
    double CenterLat,
    double CenterLng,
    double RadiusMeters,
    string? CoordinatesJson = null,
    bool IsActive = true,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record CreateRoadClosureRequest(
    string RoadName,
    string Description,
    ClosureSeverity Severity,
    string? CoordinatesJson = null,
    string BlockedReason = "",
    string? AlternateRouteAdvice = null,
    DateTimeOffset? EstimatedReopenUtc = null);

public sealed record UpdateRoadClosureRequest(
    string RoadName,
    string Description,
    ClosureSeverity Severity,
    string? CoordinatesJson = null,
    string BlockedReason = "",
    string? AlternateRouteAdvice = null,
    bool IsActive = true,
    DateTimeOffset? EstimatedReopenUtc = null);
