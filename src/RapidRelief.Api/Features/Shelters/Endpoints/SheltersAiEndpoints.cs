using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Shelters.Endpoints;

public static class SheltersAiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/shelters");

        group.MapGet("/recommend", RecommendShelterAsync).AllowAnonymous();
    }

    private static async Task<IResult> RecommendShelterAsync(
        double lat,
        double lng,
        IShelterReadService shelterReadService,
        CancellationToken ct)
    {
        // TODO: Integrate IAiAnalysisService here in the future when the contract is updated.
        // For now, per D-022 and F3 plan, explicitly fall back to nearest available shelter.

        var origin = new GeoPoint(lat, lng);
        var nearest = await shelterReadService.GetNearestAsync(origin, 5, ct);

        // Try to find the closest open shelter, otherwise closest full/closed shelter
        var recommended = nearest.FirstOrDefault(s => s.IsOpen) ?? nearest.FirstOrDefault();

        if (recommended is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No shelters found nearby.");
        }

        return Results.Ok(new ApiEnvelope<ShelterSummaryDto>(recommended));
    }
}
