using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record ReliefDelivered(
    Guid ReliefRequestId,
    Guid DispatchId,
    Guid ResourceId,
    double Quantity) : EventBase;
