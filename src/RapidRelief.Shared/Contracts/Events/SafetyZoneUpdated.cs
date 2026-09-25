using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record SafetyZoneUpdated(
    Guid ZoneId,
    string Name,
    ZoneType ZoneType,
    Severity Severity,
    bool IsActive) : EventBase;
