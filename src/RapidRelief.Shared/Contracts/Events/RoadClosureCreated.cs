using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record RoadClosureCreated(
    Guid ClosureId,
    string RoadName,
    ClosureSeverity Severity,
    string BlockedReason) : EventBase;
