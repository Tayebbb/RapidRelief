using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record IncidentClassificationOverridden(
    Guid IncidentId,
    Guid OfficerId,
    DisasterType PreviousType,
    DisasterType NewType,
    Severity PreviousSeverity,
    Severity NewSeverity,
    string Reason,
    DateTimeOffset OverriddenAtUtc
) : EventBase;
