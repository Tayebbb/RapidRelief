using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record IncidentVerified(Guid IncidentId, Guid VerifiedByUserId, bool Approved, string? Reason) : EventBase;
