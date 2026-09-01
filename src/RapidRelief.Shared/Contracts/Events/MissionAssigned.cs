using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record MissionAssigned(Guid MissionId, Guid IncidentId, Guid TeamId, Guid AssignedByUserId) : EventBase;
