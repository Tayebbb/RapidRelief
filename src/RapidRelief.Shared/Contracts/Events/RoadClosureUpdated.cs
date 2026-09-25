using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record RoadClosureUpdated(
    Guid ClosureId,
    string RoadName,
    ClosureSeverity Severity,
    bool IsActive) : EventBase;
