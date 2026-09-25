using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RapidRelief.Api.Features.SafetyZones.Data;
using RapidRelief.Api.Features.SafetyZones.Domain;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.SafetyZones.Services;

public static class SafetyZonesSeeder
{
    public static readonly IReadOnlyList<SafetyZone> SeedZones =
    [
        new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555550001"),
            Name = "Mirpur Flood Danger Zone",
            Description = "Severe water-logging and flash flooding along Mirpur-10 round-about and Section-11 lowlands.",
            ZoneType = ZoneType.DangerZone,
            Severity = Severity.Severe,
            GeometryType = "Circle",
            CenterLat = 23.8071,
            CenterLng = 90.3686,
            RadiusMeters = 850,
            CoordinatesJson = "[]",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-10),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(2)
        },
        new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555550002"),
            Name = "Savar Industrial Evacuation Perimeter",
            Description = "Structure compromised due to industrial hazard; mandatory 600m evacuation zone established.",
            ZoneType = ZoneType.EvacuationZone,
            Severity = Severity.Catastrophic,
            GeometryType = "Circle",
            CenterLat = 23.8442,
            CenterLng = 90.2689,
            RadiusMeters = 600,
            CoordinatesJson = "[]",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-18),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(3)
        },
        new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555550003"),
            Name = "Dhanmondi Lake Safe Assembly Point",
            Description = "Open green field and high-elevation emergency staging ground equipped with clean water and medical tents.",
            ZoneType = ZoneType.SafeAssemblyPoint,
            Severity = Severity.Minimal,
            GeometryType = "Circle",
            CenterLat = 23.7465,
            CenterLng = 90.3776,
            RadiusMeters = 350,
            CoordinatesJson = "[]",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
        },
        new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555550004"),
            Name = "Turag Basin Inundation Zone",
            Description = "Rapidly rising water levels along the Turag river basin posing imminent flood risks.",
            ZoneType = ZoneType.DangerZone,
            Severity = Severity.Moderate,
            GeometryType = "Polygon",
            CenterLat = 23.8340,
            CenterLng = 90.3320,
            RadiusMeters = 0,
            CoordinatesJson = "[[23.8310,90.3280],[23.8390,90.3310],[23.8360,90.3420],[23.8280,90.3350]]",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-14),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        },
        new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555550005"),
            Name = "Mohakhali Emergency Response Corridor",
            Description = "Emergency response and disaster recovery corridor reserved strictly for authorized emergency vehicles.",
            ZoneType = ZoneType.RestrictedArea,
            Severity = Severity.Minor,
            GeometryType = "Circle",
            CenterLat = 23.7778,
            CenterLng = 90.4055,
            RadiusMeters = 450,
            CoordinatesJson = "[]",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-8)
        }
    ];

    public static readonly IReadOnlyList<RoadClosure> SeedClosures =
    [
        new()
        {
            Id = Guid.Parse("22222222-3333-4444-5555-666666660001"),
            RoadName = "Rampura Bridge Crossing",
            Description = "Severe structural water overflow and dangerous currents.",
            Severity = ClosureSeverity.Flooded,
            BlockedReason = "Water level rose 2.2m above bridge deck following continuous torrential downpour.",
            AlternateRouteAdvice = "Divert via Hatirjheel bypass road or Pragati Sarani north-bound link.",
            CoordinatesJson = "[[23.7615,90.4200],[23.7635,90.4245]]",
            IsActive = true,
            ReportedAtUtc = DateTimeOffset.UtcNow.AddHours(-6),
            EstimatedReopenUtc = DateTimeOffset.UtcNow.AddHours(12)
        },
        new()
        {
            Id = Guid.Parse("22222222-3333-4444-5555-666666660002"),
            RoadName = "Kawran Bazar - Kazi Nazrul Islam Ave",
            Description = "Heavy debris and downed electric poles obstructing dual carriageways.",
            Severity = ClosureSeverity.DebrisBlocked,
            BlockedReason = "Storm-induced structural collapse and downed high-voltage power lines.",
            AlternateRouteAdvice = "Use Green Road or Panthapath connector to bypass.",
            CoordinatesJson = "[[23.7505,90.3920],[23.7540,90.3940]]",
            IsActive = true,
            ReportedAtUtc = DateTimeOffset.UtcNow.AddHours(-4),
            EstimatedReopenUtc = DateTimeOffset.UtcNow.AddHours(6)
        },
        new()
        {
            Id = Guid.Parse("22222222-3333-4444-5555-666666660003"),
            RoadName = "Gabtoli Embankment Road",
            Description = "Embankment erosion and partial road subsidence.",
            Severity = ClosureSeverity.Impasse,
            BlockedReason = "Riverbank scouring caused 40m road section collapse.",
            AlternateRouteAdvice = "Heavy freight vehicles take Aminbazar bypass.",
            CoordinatesJson = "[[23.7845,90.3440],[23.7885,90.3475]]",
            IsActive = true,
            ReportedAtUtc = DateTimeOffset.UtcNow.AddHours(-12),
            EstimatedReopenUtc = DateTimeOffset.UtcNow.AddHours(24)
        }
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<SafetyZonesDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SafetyZonesSeeder");

        if (await db.SafetyZones.AnyAsync(ct))
        {
            return;
        }

        logger.LogInformation("Seeding deterministic Dhaka safety zones and road closures...");

        db.SafetyZones.AddRange(SeedZones);
        db.RoadClosures.AddRange(SeedClosures);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {ZoneCount} safety zones and {ClosureCount} road closures.",
            SeedZones.Count, SeedClosures.Count);
    }
}
