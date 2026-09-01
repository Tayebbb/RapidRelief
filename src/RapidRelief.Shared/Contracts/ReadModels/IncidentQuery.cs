using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record IncidentQuery(IncidentStatus? Status = null, DisasterType? Type = null,
    Severity? MinSeverity = null, int Page = 1, int PageSize = 50);
