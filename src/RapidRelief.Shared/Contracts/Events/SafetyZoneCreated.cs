using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record SafetyZoneCreated(
    Guid ZoneId,
    string Name,
    ZoneType ZoneType,
    Severity Severity,
    double CenterLat,
    double CenterLng,
    double RadiusMeters) : EventBase;
