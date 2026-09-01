using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record IncidentCreated(Guid IncidentId, Guid ReporterUserId, DisasterType Type,
    Severity ReportedSeverity, GeoPoint Location, string Description, bool IsSos,
    IReadOnlyList<string> PhotoPaths) : EventBase;
