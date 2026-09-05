using Microsoft.EntityFrameworkCore;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai.Pipeline;

/// <summary>A flagged pair with the evidence behind it — never an instruction to delete anything.</summary>
public sealed record DuplicateMatch(Guid IncidentId, double Confidence, string Reason);

/// <summary>
/// D-022 duplicate rule against F8's own snapshot columns: Haversine ≤ 300 m ∧ same declared
/// type ∧ |Δ ReportedAtUtc| ≤ 30 min ∧ not self. Candidates are then scored on proximity, time
/// gap and description overlap; the highest-confidence match wins and is FLAGGED, never removed —
/// two neighbours reporting the same fire and one family reporting twice look identical from here.
/// Candidates are re-checked against IIncidentReadService — disqualified ONLY on
/// Resolved/Rejected; unknown (null) keeps them.
/// </summary>
public sealed class DuplicateDetector
{
    private const double MaxDistanceMeters = 300;
    private static readonly TimeSpan MaxTimeGap = TimeSpan.FromMinutes(30);

    /// <summary>Below this the pair is too weak to be worth an operator's attention.</summary>
    private const double MinConfidence = 0.4;

    /// <summary>Credit for clearing all three hard gates, before any ranking refinement.</summary>
    private const double GateFloor = 0.5;

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
        => (await FindMatchAsync(incidentId, location, declaredType, reportedAtUtc, description: null, ct))?.IncidentId;

    public async Task<DuplicateMatch?> FindMatchAsync(
        Guid incidentId,
        GeoPoint location,
        DisasterType declaredType,
        DateTimeOffset reportedAtUtc,
        string? description,
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
            .Select(a => new
            {
                a.IncidentId,
                a.SnapshotLatitude,
                a.SnapshotLongitude,
                a.SnapshotReportedAtUtc,
                a.SnapshotDescriptionKey,
            })
            .ToListAsync(ct);

        var key = IncidentSignalReader.Normalise(description);

        var ranked = candidates
            .Select(c => new
            {
                c.IncidentId,
                Distance = GeoMath.HaversineMeters(location, new GeoPoint(c.SnapshotLatitude, c.SnapshotLongitude)),
                Gap = (reportedAtUtc - c.SnapshotReportedAtUtc).Duration(),
                TextOverlap = IncidentSignalReader.Similarity(key, c.SnapshotDescriptionKey ?? string.Empty),
            })
            .Where(c => c.Distance <= MaxDistanceMeters)
            .Select(c => new
            {
                c.IncidentId,
                c.Distance,
                c.Gap,
                c.TextOverlap,
                Confidence = Score(c.Distance, c.Gap, c.TextOverlap),
            })
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.Distance)
            .ThenBy(c => c.IncidentId)
            .ToList();

        foreach (var candidate in ranked)
        {
            var incident = await _incidents.GetByIdAsync(candidate.IncidentId, ct);
            if (incident is { Status: IncidentStatus.Resolved or IncidentStatus.Rejected })
            {
                continue;
            }

            if (candidate.Confidence < MinConfidence)
            {
                // Ranked descending, so nothing further down can clear the bar either.
                return null;
            }

            return new DuplicateMatch(candidate.IncidentId, candidate.Confidence,
                Reason(candidate.Distance, candidate.Gap, candidate.TextOverlap));
        }

        return null;
    }

    /// <summary>
    /// Clearing the three hard gates (same type, within 300 m, within 30 min) is itself the
    /// strongest evidence, so it carries a floor; proximity, recency and wording overlap then
    /// sharpen the ranking. A pair on the exact boundary still gets flagged — that is the whole
    /// point of a review queue.
    /// </summary>
    private static double Score(double meters, TimeSpan gap, double textOverlap)
    {
        var proximity = 1 - Math.Min(1, meters / MaxDistanceMeters);
        var recency = 1 - Math.Min(1, gap.TotalMinutes / MaxTimeGap.TotalMinutes);
        var score = GateFloor + (0.2 * proximity) + (0.15 * recency) + (0.15 * textOverlap);
        return Math.Round(Math.Clamp(score, 0, 1), 3);
    }

    private static string Reason(double meters, TimeSpan gap, double textOverlap)
    {
        var parts = new List<string>
        {
            $"{meters:F0} m apart",
            gap.TotalMinutes < 1 ? "reported within the same minute" : $"reported {gap.TotalMinutes:F0} min apart",
            "same disaster type",
        };

        if (textOverlap >= 0.5)
        {
            parts.Add($"descriptions share {textOverlap:P0} of their words");
        }
        else if (textOverlap > 0)
        {
            parts.Add($"descriptions overlap only {textOverlap:P0} — wording differs");
        }
        else
        {
            parts.Add("no wording overlap, so this may be two separate calls");
        }

        return string.Join("; ", parts) + ". Flagged for review — neither report has been changed.";
    }
}
