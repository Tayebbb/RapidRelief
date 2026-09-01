using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

/// <summary>Sample-slice demo event.</summary>
public sealed record PingCreated(Guid PingId, string Message) : EventBase;
