using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using Severity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Features.Incidents.Endpoints;

/// <summary>
/// Command-centre analytics over the incident store. Read-only and cheap, so it sits outside the
/// "reports" ingestion budget — a dashboard that refreshes must not consume a citizen's quota.
/// </summary>
public static class IncidentOpsEndpoints
{
    public const string BasePath = "/api/incidents/ops";

    /// <summary>An area is "escalating" when recent volume is at least this multiple of the prior window.</summary>
    private const double EscalationFactor = 1.5;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization(AuthPolicies.RequireResponder);

        group.MapGet("/summary", SummaryAsync);
    }

    private static async Task<IResult> SummaryAsync(
        IncidentsDbContext db,
        DatabaseHealth health,
        TimeProvider clock,
        CancellationToken ct,
        int days = 14)
    {
        if (health.PostgresAvailable != true)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Database unavailable",
                detail: "Operational metrics are unavailable while the database is offline.");
        }

        days = Math.Clamp(days, 1, 90);
        var now = clock.GetUtcNow();

        var rows = await db.Reports.AsNoTracking()
            .Select(x => new Row(
                x.Status, x.DisasterType, x.Severity, x.IsSos, x.AssignedMissionId != null,
                x.Latitude, x.Longitude, x.AddressOrArea,
                x.CreatedAtUtc, x.AssignedAtUtc, x.ResolvedAtUtc))
            .ToListAsync(ct);

        var open = rows.Where(IsOpen).ToList();
        var closed = rows.Where(r => r.Status is IncidentStatus.Resolved).ToList();

        var kpi = new OpsKpiDto(
            ActiveIncidents: open.Count,
            CriticalIncidents: open.Count(IsCritical),
            SosOpen: open.Count(r => r.IsSos),
            Unassigned: open.Count(r => !r.HasMission),
            AwaitingTeam: open.Count(r => r.Status == IncidentStatus.Verified && !r.HasMission),
            InProgress: open.Count(r => r.Status is IncidentStatus.Assigned or IncidentStatus.InProgress),
            ResolvedLast24h: closed.Count(r => r.ResolvedAtUtc >= now.AddHours(-24)),
            NewLast24h: rows.Count(r => r.CreatedAtUtc >= now.AddHours(-24)),
            AvgResponseMinutes: AverageMinutes(rows
                .Where(r => r.AssignedAtUtc is not null)
                .Select(r => r.AssignedAtUtc!.Value - r.CreatedAtUtc)),
            AvgResolutionMinutes: AverageMinutes(closed
                .Where(r => r.ResolvedAtUtc is not null)
                .Select(r => r.ResolvedAtUtc!.Value - r.CreatedAtUtc)),
            ResolutionRatePercent: rows.Count == 0 ? 0 : Math.Round(closed.Count * 100.0 / rows.Count, 1),
            TotalIncidents: rows.Count);

        var byStatus = Enum.GetValues<IncidentStatus>()
            .Select(s => new NamedCountDto(s.ToString(), rows.Count(r => r.Status == s)))
            .Where(x => x.Count > 0)
            .ToList();

        var byType = rows.GroupBy(r => r.Type)
            .Select(g => new NamedCountDto(g.Key.ToString(), g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var bySeverity = Enum.GetValues<Severity>()
            .Select(s => new NamedCountDto(s.ToString(), rows.Count(r => r.Severity == s)))
            .ToList();

        var firstDay = DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(-(days - 1)));
        var daily = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new TimeBucketDto(
                day.ToString("yyyy-MM-dd"),
                rows.Count(r => DateOnly.FromDateTime(r.CreatedAtUtc.UtcDateTime) == day),
                rows.Count(r => r.ResolvedAtUtc is { } at && DateOnly.FromDateTime(at.UtcDateTime) == day)))
            .ToList();

        var recentWindow = now.AddHours(-6);
        var previousWindow = now.AddHours(-12);
        var hotspots = rows
            .Where(r => IsOpen(r) || r.ResolvedAtUtc >= previousWindow)
            .GroupBy(AreaKey)
            .Select(g =>
            {
                var recent = g.Count(r => r.CreatedAtUtc >= recentWindow);
                var previous = g.Count(r => r.CreatedAtUtc >= previousWindow && r.CreatedAtUtc < recentWindow);
                return new HotspotDto(
                    Area: g.Key,
                    Location: new GeoPoint(g.Average(r => r.Latitude), g.Average(r => r.Longitude)),
                    Total: g.Count(),
                    Critical: g.Count(IsCritical),
                    Last6h: recent,
                    Previous6h: previous,
                    Trend: Trend(recent, previous));
            })
            .OrderByDescending(h => h.Trend == "Escalating")
            .ThenByDescending(h => h.Critical)
            .ThenByDescending(h => h.Total)
            .Take(8)
            .ToList();

        return Results.Ok(new ApiEnvelope<IncidentOpsSummaryDto>(new IncidentOpsSummaryDto(
            kpi, byStatus, byType, bySeverity, daily, hotspots, now)));
    }

    private static bool IsOpen(Row r) =>
        r.Status is not (IncidentStatus.Resolved or IncidentStatus.Rejected);

    private static bool IsCritical(Row r) => r.IsSos || r.Severity >= Severity.Severe;

    private static string AreaKey(Row r) => string.IsNullOrWhiteSpace(r.AddressOrArea)
        ? $"{r.Latitude:F2}, {r.Longitude:F2}"
        : r.AddressOrArea.Trim();

    private static string Trend(int recent, int previous) => recent switch
    {
        0 => "Quiet",
        _ when previous == 0 => "Escalating",
        _ when recent >= previous * EscalationFactor => "Escalating",
        _ when recent < previous => "Easing",
        _ => "Steady",
    };

    private static double? AverageMinutes(IEnumerable<TimeSpan> spans)
    {
        var values = spans.Where(s => s > TimeSpan.Zero).Select(s => s.TotalMinutes).ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 1);
    }

    private sealed record Row(
        IncidentStatus Status,
        DisasterType Type,
        Severity Severity,
        bool IsSos,
        bool HasMission,
        double Latitude,
        double Longitude,
        string AddressOrArea,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? AssignedAtUtc,
        DateTimeOffset? ResolvedAtUtc);
}

public sealed record OpsKpiDto(
    int ActiveIncidents,
    int CriticalIncidents,
    int SosOpen,
    int Unassigned,
    int AwaitingTeam,
    int InProgress,
    int ResolvedLast24h,
    int NewLast24h,
    double? AvgResponseMinutes,
    double? AvgResolutionMinutes,
    double ResolutionRatePercent,
    int TotalIncidents);

public sealed record NamedCountDto(string Key, int Count);

public sealed record TimeBucketDto(string Day, int Reported, int Resolved);

public sealed record HotspotDto(
    string Area,
    GeoPoint Location,
    int Total,
    int Critical,
    int Last6h,
    int Previous6h,
    string Trend);

public sealed record IncidentOpsSummaryDto(
    OpsKpiDto Kpi,
    IReadOnlyList<NamedCountDto> ByStatus,
    IReadOnlyList<NamedCountDto> ByType,
    IReadOnlyList<NamedCountDto> BySeverity,
    IReadOnlyList<TimeBucketDto> Daily,
    IReadOnlyList<HotspotDto> Hotspots,
    DateTimeOffset GeneratedAtUtc);
