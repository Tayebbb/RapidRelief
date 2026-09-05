using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Services;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Shelters.Endpoints;

public sealed record ShelterRecommendationDto(
    Guid Id,
    string Name,
    GeoPoint Location,
    int Capacity,
    int Occupancy,
    IReadOnlyList<string> Facilities,
    double DistanceKm,
    int FreeSpaces,
    int OccupancyPercent,
    IReadOnlyList<string> Reasons);

public static class SheltersAiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/shelters");

        group.MapGet("/recommend", RecommendShelterAsync).AllowAnonymous();
        group.MapGet("/recommendations", RecommendationsAsync).AllowAnonymous();
    }

    private static async Task<IResult> RecommendShelterAsync(
        double lat,
        double lng,
        OpsDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        var ranked = await RankAsync(lat, lng, 1, db, databaseHealth, ct);
        if (ranked is null)
        {
            return DatabaseUnavailable();
        }

        if (ranked.Count == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No shelters found nearby.");
        }

        var best = ranked[0].Shelter;
        return Results.Ok(new ApiEnvelope<ShelterSummaryDto>(new ShelterSummaryDto(
            best.Id, best.Name, best.Location, best.Capacity, best.CurrentOccupancy, true)));
    }

    /// <summary>Ranked by suitability (distance + free capacity + facilities), with the reasons.</summary>
    private static async Task<IResult> RecommendationsAsync(
        double lat,
        double lng,
        OpsDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct,
        int count = 3)
    {
        var ranked = await RankAsync(lat, lng, count, db, databaseHealth, ct);
        if (ranked is null)
        {
            return DatabaseUnavailable();
        }

        var payload = ranked
            .Select(x => new ShelterRecommendationDto(
                x.Shelter.Id,
                x.Shelter.Name,
                x.Shelter.Location,
                x.Shelter.Capacity,
                x.Shelter.CurrentOccupancy,
                x.Shelter.Facilities,
                Math.Round(x.DistanceKm, 2),
                x.FreeSpaces,
                (int)Math.Round(x.OccupancyRatio * 100),
                x.Reasons))
            .ToList();

        return Results.Ok(new ApiEnvelope<List<ShelterRecommendationDto>>(payload));
    }

    private static async Task<IReadOnlyList<ShelterSuitability>?> RankAsync(
        double lat,
        double lng,
        int count,
        OpsDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return null;
        }

        // Feature-local read: the frozen contract DTO carries no facilities, and the citizen needs
        // to know what the shelter actually offers. Demo-scale table, loaded once per request.
        var candidates = await db.Shelters.AsNoTracking().ToListAsync(ct);
        return ShelterSuitabilityScorer.Rank(new GeoPoint(lat, lng), candidates, count);
    }

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): shelter data is temporarily unavailable.");
}
