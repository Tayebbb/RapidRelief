using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record ReliefDispatchCreated(
    Guid ReliefRequestId,
    Guid DispatchId,
    Guid ResourceId,
    double Quantity) : EventBase;
