using System.Security.Claims;
using RapidRelief.Api.Infrastructure.Modules;
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
        // Static ok for now; chunk 2 wires DatabaseHealth into DbConnected.
        endpoints.MapGet("/health", () => Results.Ok(new HealthResponse("ok", null)));

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
