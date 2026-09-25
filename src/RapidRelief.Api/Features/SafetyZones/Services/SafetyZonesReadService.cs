using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.SafetyZones.Data;
using RapidRelief.Api.Features.SafetyZones.Domain;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.SafetyZones.Services;

public sealed class SafetyZonesReadService(SafetyZonesDbContext db, DatabaseHealth health) : ISafetyZonesReadService
{
    public async Task<IReadOnlyList<SafetyZoneDto>> GetActiveSafetyZonesAsync(CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return SafetyZonesSeeder.SeedZones
                .Where(z => z.IsActive)
                .Select(ToDto)
                .ToList();
        }

        var zones = await db.SafetyZones
            .AsNoTracking()
            .Where(z => z.IsActive)
            .OrderByDescending(z => z.CreatedAtUtc)
            .ToListAsync(ct);

        return zones.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<RoadClosureDto>> GetActiveRoadClosuresAsync(CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return SafetyZonesSeeder.SeedClosures
                .Where(c => c.IsActive)
                .Select(ToDto)
                .ToList();
        }

        var closures = await db.RoadClosures
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.ReportedAtUtc)
            .ToListAsync(ct);

        return closures.Select(ToDto).ToList();
    }

    public static SafetyZoneDto ToDto(SafetyZone z) => new(
        z.Id,
        z.Name,
        z.Description,
        z.ZoneType,
        z.Severity,
        z.GeometryType,
        z.CenterLat,
        z.CenterLng,
        z.RadiusMeters,
        z.CoordinatesJson,
        z.IsActive,
        z.CreatedAtUtc,
        z.ExpiresAtUtc);

    public static RoadClosureDto ToDto(RoadClosure c) => new(
        c.Id,
        c.RoadName,
        c.Description,
        c.Severity,
        c.CoordinatesJson,
        c.BlockedReason,
        c.AlternateRouteAdvice,
        c.IsActive,
        c.ReportedAtUtc,
        c.EstimatedReopenUtc);
}
