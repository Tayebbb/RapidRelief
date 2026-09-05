using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record AiAnalysisRequest(Guid IncidentId, DisasterType ReportedType, string Description,
    GeoPoint Location, bool IsSos, DateTimeOffset ReportedAtUtc, IReadOnlyList<string> PhotoPaths,
    Severity ReportedSeverity = Severity.Minor, int AffectedPeopleCount = 0);
