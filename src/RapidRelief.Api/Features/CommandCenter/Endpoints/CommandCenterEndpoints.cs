using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;
using RapidRelief.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;

namespace RapidRelief.Api.Features.CommandCenter.Endpoints;

public static class CommandCenterEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/command-center")
                             .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapGet("/overview", GetOverviewAsync);
    }

    private static async Task<IResult> GetOverviewAsync(
        IIncidentReadService incidentReadService,
        IShelterReadService shelterReadService,
        IRegistryReadService registryReadService,
        RapidRelief.Api.Infrastructure.Persistence.DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Database unavailable",
                detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so database-backed endpoints are temporarily unavailable.");
        }

        // Fetch data concurrently for performance
        var incidentsTask = incidentReadService.GetIncidentsAsync(new IncidentQuery(), ct);
        var sheltersTask = shelterReadService.GetSheltersAsync(ct);
        var hospitalsTask = registryReadService.GetHospitalsAsync(ct);
        var volunteersTask = registryReadService.GetVolunteersAsync(ct);
        var ngosTask = registryReadService.GetNgosAsync(ct);

        await Task.WhenAll(incidentsTask, sheltersTask, hospitalsTask, volunteersTask, ngosTask);

        var incidents = incidentsTask.Result.Items;
        var shelters = sheltersTask.Result;
        var hospitals = hospitalsTask.Result;
        var volunteers = volunteersTask.Result;
        var ngos = ngosTask.Result;

        var totalActiveIncidents = incidents.Count(i => i.Status != IncidentStatus.Resolved);
        var totalCriticalIncidents = incidents.Count(i => (i.Severity == Severity.Severe || i.Severity == Severity.Catastrophic) && i.Status != IncidentStatus.Resolved);
        
        var openShelters = shelters.Where(s => s.IsOpen).ToList();
        var totalShelterCapacity = openShelters.Sum(s => Math.Max(0, s.Capacity - s.Occupancy));

        var dto = new CommandCenterOverviewDto(
            TotalActiveIncidents: totalActiveIncidents,
            TotalCriticalIncidents: totalCriticalIncidents,
            TotalOpenShelters: openShelters.Count,
            TotalShelterCapacity: totalShelterCapacity,
            TotalHospitals: hospitals.Count,
            TotalVolunteers: volunteers.Count,
            TotalNgos: ngos.Count
        );

        return Results.Ok(new ApiEnvelope<CommandCenterOverviewDto>(dto));
    }
}
