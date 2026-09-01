using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record MissionStatusChanged(Guid MissionId, Guid IncidentId, MissionStatus NewStatus) : EventBase;
