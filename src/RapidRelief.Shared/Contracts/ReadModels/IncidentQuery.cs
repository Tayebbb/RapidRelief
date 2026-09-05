using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Shared.Contracts.ReadModels;

/// <summary>
/// <paramref name="Near"/> with <paramref name="RadiusKm"/> restricts the result to a circle;
/// <paramref name="OpenOnly"/> drops Resolved and Rejected. Both default to off so existing
/// callers are unaffected.
/// </summary>
public sealed record IncidentQuery(IncidentStatus? Status = null, DisasterType? Type = null,
    Severity? MinSeverity = null, int Page = 1, int PageSize = 50,
    GeoPoint? Near = null, double? RadiusKm = null, bool OpenOnly = false);
