using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record ReliefRequested(Guid RequestId, Guid RequesterUserId, ResourceType Type,
    int Quantity, GeoPoint Location, int UrgencyLevel) : EventBase;
