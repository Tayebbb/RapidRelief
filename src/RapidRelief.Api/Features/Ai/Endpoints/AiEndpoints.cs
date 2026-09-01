using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai.Endpoints;

// Feature-local wire records (D-019 precedent) — the frozen AiAssessmentDto stays untouched.

public sealed record AssessmentResponse(
    Guid IncidentId,
    DisasterType PredictedType,
    Severity EstimatedSeverity,
    double PriorityScore,
    string Summary,
    Guid? PossibleDuplicateOfId,
    string Provider,
    string? ModelName,
    int LatencyMs,
    DateTimeOffset CreatedAtUtc);

public sealed record RecommendationCandidate(Guid Id, string Name, double? DistanceKm, string Detail);

public sealed record RecommendationResponse(
    Guid IncidentId,
    string Kind,
    string SourcedFrom,
    string? Reason,
    IReadOnlyList<RecommendationCandidate> Candidates);

/// <summary>
/// /api/ai surface: any authenticated role, "ai" rate-limit policy, no-store responses
/// (assessment data is operational, never cacheable). Recommendation sources per D-027.
/// </summary>
public static class AiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ai")
            .RequireAuthorization()
            .RequireRateLimiting("ai");
        group.AddEndpointFilter(CacheControlNoStoreFilter);

        group.MapGet("/assessments/{incidentId:guid}", GetAssessmentAsync);
        group.MapGet("/recommendations/shelter", GetShelterRecommendationsAsync);
        group.MapGet("/recommendations/team", GetTeamRecommendationsAsync);
        group.MapGet("/recommendations/resource", GetResourceRecommendationsAsync);
    }

    private static async Task<IResult> GetAssessmentAsync(
        Guid incidentId,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var row = await db.Assessments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.IncidentId == incidentId, ct);
        if (row is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Assessment not found",
                detail: "The incident has not been assessed yet.");
        }

        var response = new AssessmentResponse(row.IncidentId, row.PredictedType, row.EstimatedSeverity,
            row.PriorityScore, row.Summary, row.PossibleDuplicateOfId, row.Provider, row.ModelName,
            row.LatencyMs, row.CreatedAtUtc);
        return Results.Ok(new ApiEnvelope<AssessmentResponse>(response));
    }

    private static async Task<IResult> GetShelterRecommendationsAsync(
        Guid incidentId,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        IIncidentReadService incidents,
        IShelterReadService shelters,
        CancellationToken ct)
    {
        var resolved = await ResolveIncidentAsync(incidentId, db, databaseHealth, incidents, ct);
        if (resolved is null)
        {
            return IncidentNotFound();
        }

        var (origin, _) = resolved.Value;
        var nearest = await shelters.GetNearestAsync(origin, count: 50, ct);
        var candidates = nearest
            .Where(s => s.IsOpen && s.Occupancy < s.Capacity)
            .Take(3)
            .Select(s => new RecommendationCandidate(s.Id, s.Name,
                DistanceKm(origin, s.Location), $"free capacity {s.Capacity - s.Occupancy}"))
            .ToList();

        return Results.Ok(new ApiEnvelope<RecommendationResponse>(
            new RecommendationResponse(incidentId, "Shelter", "ShelterReadService", Reason: null, candidates)));
    }

    private static async Task<IResult> GetTeamRecommendationsAsync(
        Guid incidentId,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        IIncidentReadService incidents,
        IRegistryReadService registry,
        CancellationToken ct)
    {
        var resolved = await ResolveIncidentAsync(incidentId, db, databaseHealth, incidents, ct);
        if (resolved is null)
        {
            return IncidentNotFound();
        }

        var (origin, type) = resolved.Value;
        var wantedSkills = TeamSkills(type);
        var volunteers = await registry.GetVolunteersAsync(ct);
        var reachable = volunteers.Where(v => v.IsAvailable && v.Location is not null).ToList();

        var matched = reachable
            .Select(v => (Volunteer: v,
                Matched: v.Skills.Where(s => wantedSkills.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList()))
            .Where(x => x.Matched.Count > 0)
            .OrderBy(x => GeoMath.HaversineMeters(origin, x.Volunteer.Location!))
            .ThenBy(x => x.Volunteer.Id)
            .Take(3)
            .ToList();

        string? reason = null;
        List<RecommendationCandidate> candidates;
        if (matched.Count > 0)
        {
            candidates = matched
                .Select(x => new RecommendationCandidate(x.Volunteer.Id, x.Volunteer.Name,
                    DistanceKm(origin, x.Volunteer.Location!), string.Join(", ", x.Matched)))
                .ToList();
        }
        else
        {
            // D-027: no skill match → nearest available volunteers with a location.
            reason = "NoSkillMatch";
            candidates = reachable
                .OrderBy(v => GeoMath.HaversineMeters(origin, v.Location!))
                .ThenBy(v => v.Id)
                .Take(3)
                .Select(v => new RecommendationCandidate(v.Id, v.Name, DistanceKm(origin, v.Location!), ""))
                .ToList();
        }

        return Results.Ok(new ApiEnvelope<RecommendationResponse>(
            new RecommendationResponse(incidentId, "Team", "VolunteerRegistry", reason, candidates)));
    }

    private static async Task<IResult> GetResourceRecommendationsAsync(
        Guid incidentId,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        IIncidentReadService incidents,
        IRegistryReadService registry,
        CancellationToken ct)
    {
        var resolved = await ResolveIncidentAsync(incidentId, db, databaseHealth, incidents, ct);
        if (resolved is null)
        {
            return IncidentNotFound();
        }

        var (_, type) = resolved.Value;
        var wantedFocus = FocusAreas(type);
        var ngos = await registry.GetNgosAsync(ct);

        var matched = ngos
            .Select(n => (Ngo: n,
                Matched: n.FocusAreas.Where(f => wantedFocus.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList()))
            .Where(x => x.Matched.Count > 0)
            .Take(3) // seed order preserved (D-027)
            .ToList();

        string? reason = null;
        List<RecommendationCandidate> candidates;
        if (matched.Count > 0)
        {
            candidates = matched
                .Select(x => new RecommendationCandidate(x.Ngo.Id, x.Ngo.Name, DistanceKm: null,
                    string.Join(", ", x.Matched)))
                .ToList();
        }
        else
        {
            reason = "NoFocusMatch";
            candidates = ngos
                .Take(3)
                .Select(n => new RecommendationCandidate(n.Id, n.Name, DistanceKm: null, ""))
                .ToList();
        }

        return Results.Ok(new ApiEnvelope<RecommendationResponse>(
            new RecommendationResponse(incidentId, "Resource", "NgoRegistry", reason, candidates)));
    }

    /// <summary>Own snapshot row first (skipped while degraded), then the incident read contract, else null → 404.</summary>
    private static async Task<(GeoPoint Origin, DisasterType Type)?> ResolveIncidentAsync(
        Guid incidentId,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        IIncidentReadService incidents,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable == true)
        {
            var snapshot = await db.Assessments.AsNoTracking()
                .Where(a => a.IncidentId == incidentId)
                .Select(a => new { a.SnapshotLatitude, a.SnapshotLongitude, a.SnapshotType })
                .FirstOrDefaultAsync(ct);
            if (snapshot is not null)
            {
                return (new GeoPoint(snapshot.SnapshotLatitude, snapshot.SnapshotLongitude), snapshot.SnapshotType);
            }
        }

        var incident = await incidents.GetByIdAsync(incidentId, ct);
        return incident is null ? null : (incident.Location, incident.Type);
    }

    // D-027 verbatim maps — matching is OrdinalIgnoreCase, cap 3.
    private static string[] TeamSkills(DisasterType type) => type switch
    {
        DisasterType.Flood => ["Swimming", "Boating"],
        DisasterType.Earthquake or DisasterType.BuildingCollapse => ["Rescue", "RopeWork", "HeavyLifting"],
        DisasterType.Fire => ["FirstAid", "Medical"],
        DisasterType.Cyclone => ["FirstAid", "Logistics"],
        DisasterType.Landslide => ["Rescue", "HeavyLifting"],
        _ => ["FirstAid"],
    };

    private static string[] FocusAreas(DisasterType type) => type switch
    {
        DisasterType.Flood => ["Flood Relief", "Food"],
        DisasterType.Fire => ["Medical Camps", "Health"],
        DisasterType.Earthquake or DisasterType.BuildingCollapse => ["Ambulance", "Health"],
        DisasterType.Cyclone => ["Shelter", "Food"],
        DisasterType.Landslide => ["Shelter", "Health"],
        _ => ["Micro-relief", "Food"],
    };

    private static double DistanceKm(GeoPoint origin, GeoPoint target)
        => Math.Round(GeoMath.HaversineMeters(origin, target) / 1000.0, 2);

    private static IResult IncidentNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Incident not found",
        detail: "No assessment snapshot or incident record exists for this id.");

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so database-backed endpoints are temporarily unavailable.");

    internal static async ValueTask<object?> CacheControlNoStoreFilter(
        EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        invocationContext.HttpContext.Response.Headers.CacheControl = "no-store, private";
        return await next(invocationContext);
    }
}
