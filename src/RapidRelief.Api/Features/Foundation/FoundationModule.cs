using System.Security.Claims;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

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

        // DEMO SURFACE — foundation-owned proof feed for the /sample RapidMap (stub-backed via
        // IIncidentReadService). Remove once F2/F7 expose real incident read endpoints.
        // Responder-only: this returns REAL incidents, including the precise coordinates and
        // summary of live SOS reports. It was anonymous, which handed an attacker a targeting
        // feed for vulnerable people during a disaster.
        group.MapGet("/demo-incidents", async (IIncidentReadService incidents, CancellationToken ct) =>
        {
            var result = await incidents.GetIncidentsAsync(new IncidentQuery(PageSize: 100), ct);
            return Results.Ok(new ApiEnvelope<PagedResult<IncidentSummaryDto>>(result));
        }).RequireAuthorization(AuthPolicies.RequireResponder);

        // Anonymous because the landing map renders before sign-in. The key (when a keyed
        // provider is configured) is substituted here so it never lives in client source.
        group.MapGet("/map-config", (IConfiguration config) =>
            Results.Ok(new ApiEnvelope<MapConfigDto>(ReadMapConfig(config)))).AllowAnonymous();

        // Dynamically discover all valid images in the hero images folder so adding/removing files is instant
        endpoints.MapGet("/api/hero-images", (IWebHostEnvironment env) =>
        {
            var clientDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "RapidRelief.Client", "wwwroot", "hero images"));
            var prodDir = env.WebRootPath is not null 
                ? Path.Combine(env.WebRootPath, "hero images") 
                : Path.Combine(env.ContentRootPath, "wwwroot", "hero images");
            var dir = Directory.Exists(clientDir) ? clientDir : Directory.Exists(prodDir) ? prodDir : null;

            if (dir is null || !Directory.Exists(dir))
            {
                return Results.Ok(Array.Empty<string>());
            }

            var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
            var files = Directory.EnumerateFiles(dir)
                .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .OrderBy(f => f)
                .Select(f => $"hero images/{f}")
                .ToArray();

            return Results.Ok(files);
        }).AllowAnonymous();
    }

    /// <summary>
    /// Reads <c>Map:*</c> with the OSM fallback, substituting <c>{key}</c> in the tile template
    /// from <c>Map:ApiKey</c> so the key is never present in the client bundle or in source.
    /// </summary>
    private static MapConfigDto ReadMapConfig(IConfiguration config)
    {
        var section = config.GetSection("Map");
        var fallback = MapConfigDto.Fallback;

        var tileUrl = Value(section["TileUrl"], fallback.TileUrl);
        var apiKey = section["ApiKey"];
        tileUrl = string.IsNullOrWhiteSpace(apiKey)
            ? tileUrl.Replace("{key}", string.Empty, StringComparison.Ordinal)
            : tileUrl.Replace("{key}", apiKey, StringComparison.Ordinal);

        return new MapConfigDto(
            tileUrl,
            Value(section["Attribution"], fallback.Attribution),
            Clamp(section.GetValue("MaxZoom", fallback.MaxZoom), 1, 22, fallback.MaxZoom),
            new GeoPointDto(
                section.GetValue("DefaultLatitude", fallback.DefaultCenter.Latitude),
                section.GetValue("DefaultLongitude", fallback.DefaultCenter.Longitude)),
            Clamp(section.GetValue("DefaultZoom", fallback.DefaultZoom), 1, 22, fallback.DefaultZoom));

        static string Value(string? candidate, string fallbackValue)
            => string.IsNullOrWhiteSpace(candidate) ? fallbackValue : candidate.Trim();

        static int Clamp(int candidate, int min, int max, int fallbackValue)
            => candidate < min || candidate > max ? fallbackValue : candidate;
    }
}

public sealed record WhoAmIResponse(string Name, string Id, IReadOnlyList<string> Roles);

public sealed record HealthResponse(string Status, bool? DbConnected);
