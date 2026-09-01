using System.Security.Claims;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Features.Foundation;

public sealed class FoundationModule : IFeatureModule
{
    public string Name => "Foundation";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // D-005: dbConnected true/false/null from DatabaseHealth; status "degraded" when the DB is down.
        endpoints.MapGet("/health", (DatabaseHealth databaseHealth) =>
            Results.Ok(new HealthResponse(
                databaseHealth.PostgresAvailable == false ? "degraded" : "ok",
                databaseHealth.PostgresAvailable)));

        var group = endpoints.MapGroup("/api/foundation");

        group.MapGet("/whoami", (ClaimsPrincipal user) =>
        {
            var response = new WhoAmIResponse(
                user.Identity?.Name ?? string.Empty,
                user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray());
            return Results.Ok(new ApiEnvelope<WhoAmIResponse>(response));
        }).RequireAuthorization();
    }
}

public sealed record WhoAmIResponse(string Name, string Id, IReadOnlyList<string> Roles);

public sealed record HealthResponse(string Status, bool? DbConnected);
