using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Incidents.Services;

/// <summary>
/// Real <see cref="IIncidentReadService"/> over incidents_reports — displaces the F0 stub so every
/// consumer (AI duplicate re-check, rescue queue, command centre) reads live data.
/// Returns empty results while degraded (D-005) so read-only consumers never throw.
/// </summary>
public sealed class IncidentReadService(IncidentsDbContext db, DatabaseHealth health) : IIncidentReadService
{
    public async Task<PagedResult<IncidentSummaryDto>> GetIncidentsAsync(IncidentQuery query, CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return new PagedResult<IncidentSummaryDto>([], query.Page, query.PageSize, 0);
        }

        var page = Math.Clamp(query.Page, 1, 1_000_000);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var filtered = db.Reports.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
        {
            filtered = filtered.Where(x => x.Status == status);
        }

        if (query.Type is { } type)
        {
            filtered = filtered.Where(x => x.DisasterType == type);
        }

        if (query.MinSeverity is { } minSeverity)
        {
            filtered = filtered.Where(x => x.Severity >= minSeverity);
        }

        if (query.OpenOnly)
        {
            filtered = filtered.Where(x =>
                x.Status != IncidentStatus.Resolved && x.Status != IncidentStatus.Rejected);
        }

        if (query.Near is { } origin && query.RadiusKm is { } radiusKm and > 0)
        {
            // Bounding box in SQL, exact Haversine in memory — no PostGIS dependency, and the
            // box keeps the in-memory pass small even on a busy day.
            var latDelta = radiusKm / 111.0;
            var lngDelta = radiusKm / (111.0 * Math.Max(0.01, Math.Cos(origin.Latitude * Math.PI / 180)));
            filtered = filtered.Where(x =>
                x.Latitude >= origin.Latitude - latDelta && x.Latitude <= origin.Latitude + latDelta
                && x.Longitude >= origin.Longitude - lngDelta && x.Longitude <= origin.Longitude + lngDelta);

            var boxed = await filtered
                .OrderByDescending(x => x.IsSos)
                .ThenByDescending(x => x.PriorityScore ?? 0)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Take(500)
                .ToListAsync(ct);

            var within = boxed
                .Where(x => Distance(origin, x.Latitude, x.Longitude) <= radiusKm)
                .ToList();

            return new PagedResult<IncidentSummaryDto>(
                within.Skip((page - 1) * pageSize).Take(pageSize).Select(ToSummary).ToList(),
                page, pageSize, within.Count);
        }

        var total = await filtered.CountAsync(ct);
        var rows = await filtered
            .OrderByDescending(x => x.IsSos)
            .ThenByDescending(x => x.PriorityScore ?? 0)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<IncidentSummaryDto>(rows.Select(ToSummary).ToList(), page, pageSize, total);
    }

    public async Task<IncidentSummaryDto?> GetByIdAsync(Guid incidentId, CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return null;
        }

        var row = await db.Reports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == incidentId, ct);
        return row is null ? null : ToSummary(row);
    }

    private static double Distance(GeoPoint origin, double latitude, double longitude)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = (latitude - origin.Latitude) * Math.PI / 180;
        var dLng = (longitude - origin.Longitude) * Math.PI / 180;
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(origin.Latitude * Math.PI / 180) * Math.Cos(latitude * Math.PI / 180)
               * Math.Sin(dLng / 2) * Math.Sin(dLng / 2));
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static IncidentSummaryDto ToSummary(Domain.IncidentReport report) => new(        report.Id,
        report.DisasterType,
        report.Severity,
        report.Status,
        new GeoPoint(report.Latitude, report.Longitude),
        string.IsNullOrWhiteSpace(report.AiSummary) ? report.Title : report.AiSummary,
        report.CreatedAtUtc,
        report.IsSos,
        report.PriorityScore);
}
