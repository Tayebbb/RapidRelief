using Microsoft.EntityFrameworkCore;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai.Pipeline;

/// <summary>
/// D-022 duplicate rule against F8's own snapshot columns: Haversine ≤ 300 m ∧ same declared
/// type ∧ |Δ ReportedAtUtc| ≤ 30 min ∧ not self; nearest wins. Candidates re-checked against
/// IIncidentReadService — disqualified ONLY on Resolved/Rejected; unknown (null) keeps them.
/// Constants live here on purpose — no config knobs.
/// </summary>
public sealed class DuplicateDetector
{
    private const double MaxDistanceMeters = 300;
    private static readonly TimeSpan MaxTimeGap = TimeSpan.FromMinutes(30);

    private readonly Data.AiDbContext _db;
    private readonly IIncidentReadService _incidents;

    public DuplicateDetector(Data.AiDbContext db, IIncidentReadService incidents)
    {
        _db = db;
        _incidents = incidents;
    }

    public async Task<Guid?> FindDuplicateAsync(
        Guid incidentId,
        GeoPoint location,
        DisasterType declaredType,
        DateTimeOffset reportedAtUtc,
        CancellationToken ct = default)
    {
        var windowStart = reportedAtUtc - MaxTimeGap;
        var windowEnd = reportedAtUtc + MaxTimeGap;

        // Type + time window translate to SQL (ticks under SQLite); distance ranks in memory.
        var candidates = await _db.Assessments.AsNoTracking()
            .Where(a => a.IncidentId != incidentId
                && a.SnapshotType == declaredType
                && a.SnapshotReportedAtUtc >= windowStart
                && a.SnapshotReportedAtUtc <= windowEnd)
            .Select(a => new { a.IncidentId, a.SnapshotLatitude, a.SnapshotLongitude })
            .ToListAsync(ct);

        var ranked = candidates
            .Select(c => new
            {
                c.IncidentId,
                Distance = GeoMath.HaversineMeters(location, new GeoPoint(c.SnapshotLatitude, c.SnapshotLongitude)),
            })
            .Where(c => c.Distance <= MaxDistanceMeters)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.IncidentId)
            .ToList();

        foreach (var candidate in ranked)
        {
            var incident = await _incidents.GetByIdAsync(candidate.IncidentId, ct);
            if (incident is { Status: IncidentStatus.Resolved or IncidentStatus.Rejected })
            {
                continue; // closed out — never a live duplicate target
            }
            return candidate.IncidentId; // nearest surviving candidate wins
        }

        return null;
    }
}
