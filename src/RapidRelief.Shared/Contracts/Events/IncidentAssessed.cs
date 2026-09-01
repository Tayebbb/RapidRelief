using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record IncidentAssessed(Guid IncidentId, Severity EstimatedSeverity,
    double PriorityScore, string Summary, Guid? PossibleDuplicateOfId) : EventBase;
