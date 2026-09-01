using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

/// <summary>Action examples: "Login", "Lock", "RoleChange", …</summary>
public sealed record AuthEvent(Guid UserId, string Action, string? Details) : EventBase;
