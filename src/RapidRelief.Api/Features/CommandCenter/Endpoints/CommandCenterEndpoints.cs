using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

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
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Database unavailable",
                detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so database-backed endpoints are temporarily unavailable.");
        }

        var incidentsTask = incidentReadService.GetIncidentsAsync(new IncidentQuery(), ct);
        var sheltersTask = shelterReadService.GetSheltersAsync(ct);

        // RegistryReadService methods all query the same scoped RegistryDbContext instance.
        // EF Core DbContext is not thread-safe, so query registry entities sequentially.
        var hospitals = await registryReadService.GetHospitalsAsync(ct);
        var volunteers = await registryReadService.GetVolunteersAsync(ct);
        var ngos = await registryReadService.GetNgosAsync(ct);

        var incidentsPage = await incidentsTask;
        var shelters = await sheltersTask;
        var incidents = incidentsPage.Items;

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
