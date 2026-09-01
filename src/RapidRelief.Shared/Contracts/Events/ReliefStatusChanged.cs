using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record ReliefStatusChanged(Guid RequestId, ReliefStatus NewStatus) : EventBase;
