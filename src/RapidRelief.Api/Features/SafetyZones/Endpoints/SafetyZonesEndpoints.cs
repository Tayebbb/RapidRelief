using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.SafetyZones.Data;
using RapidRelief.Api.Features.SafetyZones.Domain;
using RapidRelief.Api.Features.SafetyZones.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.SafetyZones.Endpoints;

public static class SafetyZonesEndpoints
{
    public const string BasePath = "/api/safety";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireRateLimiting("reports");

        // Zones
        group.MapGet("/zones", ListZonesAsync).AllowAnonymous();
        group.MapGet("/zones/{id:guid}", GetZoneAsync).AllowAnonymous();
        group.MapPost("/zones", CreateZoneAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapPut("/zones/{id:guid}", UpdateZoneAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapDelete("/zones/{id:guid}", DeleteZoneAsync).RequireAuthorization(AuthPolicies.RequireGovernment);

        // Closures
        group.MapGet("/closures", ListClosuresAsync).AllowAnonymous();
        group.MapGet("/closures/{id:guid}", GetClosureAsync).AllowAnonymous();
        group.MapPost("/closures", CreateClosureAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapPut("/closures/{id:guid}", UpdateClosureAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapDelete("/closures/{id:guid}", DeleteClosureAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
    }

    // =========================================================================
    // SAFETY ZONES
    // =========================================================================

    private static async Task<IResult> ListZonesAsync(
        SafetyZonesDbContext db,
        DatabaseHealth health,
        bool all = false,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            var seed = SafetyZonesSeeder.SeedZones
                .Where(z => all || z.IsActive)
                .Select(SafetyZonesReadService.ToDto)
                .ToList();
            return Json(new ApiEnvelope<IReadOnlyList<SafetyZoneDto>>(seed));
        }

        var query = db.SafetyZones.AsNoTracking();
        if (!all)
        {
            query = query.Where(z => z.IsActive);
        }

        var zones = await query.OrderByDescending(z => z.CreatedAtUtc).ToListAsync(ct);
        return Json(new ApiEnvelope<IReadOnlyList<SafetyZoneDto>>(zones.Select(SafetyZonesReadService.ToDto).ToList()));
    }

    private static async Task<IResult> GetZoneAsync(
        Guid id,
        SafetyZonesDbContext db,
        DatabaseHealth health,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            var stub = SafetyZonesSeeder.SeedZones.FirstOrDefault(z => z.Id == id);
            return stub is null ? Results.NotFound() : Json(new ApiEnvelope<SafetyZoneDto>(SafetyZonesReadService.ToDto(stub)));
        }

        var zone = await db.SafetyZones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == id, ct);
        return zone is null ? Results.NotFound() : Json(new ApiEnvelope<SafetyZoneDto>(SafetyZonesReadService.ToDto(zone)));
    }

    private static async Task<IResult> CreateZoneAsync(
        CreateSafetyZoneRequest request,
        IValidator<CreateSafetyZoneRequest> validator,
        SafetyZonesDbContext db,
        IAuditTrail audit,
        IEventBus events,
        IRealtimeNotifier notifier,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var actor = ExtractActor(context);

        var zone = new SafetyZone
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            ZoneType = request.ZoneType,
            Severity = request.Severity,
            GeometryType = string.IsNullOrWhiteSpace(request.GeometryType) ? "Circle" : request.GeometryType.Trim(),
            CenterLat = request.CenterLat,
            CenterLng = request.CenterLng,
            RadiusMeters = request.RadiusMeters,
            CoordinatesJson = string.IsNullOrWhiteSpace(request.CoordinatesJson) ? "[]" : request.CoordinatesJson.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

        db.SafetyZones.Add(zone);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(
            actor.Id,
            actor.Name,
            actor.Role,
            "SafetyZone.Create",
            "SafetyZone",
            zone.Id.ToString(),
            $"Established safety zone \"{zone.Name}\" ({zone.ZoneType}, severity: {zone.Severity})",
            "Created"), ct);

        await events.PublishAsync(new SafetyZoneCreated(
            zone.Id,
            zone.Name,
            zone.ZoneType,
            zone.Severity,
            zone.CenterLat,
            zone.CenterLng,
            zone.RadiusMeters), ct);

        await notifier.NotifyAllAsync(RealtimeTopics.SafetyZoneUpdated, new
        {
            title = $"Safety Zone Established: {zone.Name}",
            type = zone.ZoneType.ToString(),
            severity = zone.Severity.ToString()
        }, ct);

        return Json(new ApiEnvelope<SafetyZoneDto>(SafetyZonesReadService.ToDto(zone)), StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateZoneAsync(
        Guid id,
        UpdateSafetyZoneRequest request,
        IValidator<UpdateSafetyZoneRequest> validator,
        SafetyZonesDbContext db,
        IAuditTrail audit,
        IEventBus events,
        IRealtimeNotifier notifier,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var zone = await db.SafetyZones.FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null) return Results.NotFound();

        var actor = ExtractActor(context);

        zone.Name = request.Name.Trim();
        zone.Description = request.Description.Trim();
        zone.ZoneType = request.ZoneType;
        zone.Severity = request.Severity;
        zone.GeometryType = string.IsNullOrWhiteSpace(request.GeometryType) ? "Circle" : request.GeometryType.Trim();
        zone.CenterLat = request.CenterLat;
        zone.CenterLng = request.CenterLng;
        zone.RadiusMeters = request.RadiusMeters;
        zone.CoordinatesJson = string.IsNullOrWhiteSpace(request.CoordinatesJson) ? "[]" : request.CoordinatesJson.Trim();
        zone.IsActive = request.IsActive;
        zone.ExpiresAtUtc = request.ExpiresAtUtc;

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(
            actor.Id,
            actor.Name,
            actor.Role,
            "SafetyZone.Update",
            "SafetyZone",
            zone.Id.ToString(),
            $"Updated safety zone \"{zone.Name}\" (Active: {zone.IsActive})",
            "Updated"), ct);

        await events.PublishAsync(new SafetyZoneUpdated(
            zone.Id,
            zone.Name,
            zone.ZoneType,
            zone.Severity,
            zone.IsActive), ct);

        await notifier.NotifyAllAsync(RealtimeTopics.SafetyZoneUpdated, new
        {
            title = $"Safety Zone Updated: {zone.Name}",
            type = zone.ZoneType.ToString(),
            isActive = zone.IsActive
        }, ct);

        return Json(new ApiEnvelope<SafetyZoneDto>(SafetyZonesReadService.ToDto(zone)));
    }

    private static async Task<IResult> DeleteZoneAsync(
        Guid id,
        SafetyZonesDbContext db,
        IAuditTrail audit,
        IEventBus events,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var zone = await db.SafetyZones.FirstOrDefaultAsync(z => z.Id == id, ct);
        if (zone is null) return Results.NotFound();

        var actor = ExtractActor(context);

        db.SafetyZones.Remove(zone);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(
            actor.Id,
            actor.Name,
            actor.Role,
            "SafetyZone.Delete",
            "SafetyZone",
            zone.Id.ToString(),
            $"Deleted safety zone \"{zone.Name}\"",
            "Deleted"), ct);

        await events.PublishAsync(new SafetyZoneUpdated(
            zone.Id,
            zone.Name,
            zone.ZoneType,
            zone.Severity,
            false), ct);

        return Results.NoContent();
    }

    // =========================================================================
    // ROAD CLOSURES
    // =========================================================================

    private static async Task<IResult> ListClosuresAsync(
        SafetyZonesDbContext db,
        DatabaseHealth health,
        bool all = false,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            var seed = SafetyZonesSeeder.SeedClosures
                .Where(c => all || c.IsActive)
                .Select(SafetyZonesReadService.ToDto)
                .ToList();
            return Json(new ApiEnvelope<IReadOnlyList<RoadClosureDto>>(seed));
        }

        var query = db.RoadClosures.AsNoTracking();
        if (!all)
        {
            query = query.Where(c => c.IsActive);
        }

        var closures = await query.OrderByDescending(c => c.ReportedAtUtc).ToListAsync(ct);
        return Json(new ApiEnvelope<IReadOnlyList<RoadClosureDto>>(closures.Select(SafetyZonesReadService.ToDto).ToList()));
    }

    private static async Task<IResult> GetClosureAsync(
        Guid id,
        SafetyZonesDbContext db,
        DatabaseHealth health,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            var stub = SafetyZonesSeeder.SeedClosures.FirstOrDefault(c => c.Id == id);
            return stub is null ? Results.NotFound() : Json(new ApiEnvelope<RoadClosureDto>(SafetyZonesReadService.ToDto(stub)));
        }

        var closure = await db.RoadClosures.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return closure is null ? Results.NotFound() : Json(new ApiEnvelope<RoadClosureDto>(SafetyZonesReadService.ToDto(closure)));
    }

    private static async Task<IResult> CreateClosureAsync(
        CreateRoadClosureRequest request,
        IValidator<CreateRoadClosureRequest> validator,
        SafetyZonesDbContext db,
        IAuditTrail audit,
        IEventBus events,
        IRealtimeNotifier notifier,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var actor = ExtractActor(context);

        var closure = new RoadClosure
        {
            Id = Guid.NewGuid(),
            RoadName = request.RoadName.Trim(),
            Description = request.Description.Trim(),
            Severity = request.Severity,
            CoordinatesJson = string.IsNullOrWhiteSpace(request.CoordinatesJson) ? "[]" : request.CoordinatesJson.Trim(),
            BlockedReason = request.BlockedReason.Trim(),
            AlternateRouteAdvice = request.AlternateRouteAdvice?.Trim(),
            IsActive = true,
            ReportedAtUtc = DateTimeOffset.UtcNow,
            EstimatedReopenUtc = request.EstimatedReopenUtc
        };

        db.RoadClosures.Add(closure);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(
            actor.Id,
            actor.Name,
            actor.Role,
            "RoadClosure.Create",
            "RoadClosure",
            closure.Id.ToString(),
            $"Reported road closure on \"{closure.RoadName}\" ({closure.Severity}: {closure.BlockedReason})",
            "Created"), ct);

        await events.PublishAsync(new RoadClosureCreated(
            closure.Id,
            closure.RoadName,
            closure.Severity,
            closure.BlockedReason), ct);

        await notifier.NotifyAllAsync(RealtimeTopics.RoadClosureUpdated, new
        {
            title = $"Road Closed: {closure.RoadName}",
            severity = closure.Severity.ToString(),
            reason = closure.BlockedReason
        }, ct);

        return Json(new ApiEnvelope<RoadClosureDto>(SafetyZonesReadService.ToDto(closure)), StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateClosureAsync(
        Guid id,
        UpdateRoadClosureRequest request,
        IValidator<UpdateRoadClosureRequest> validator,
        SafetyZonesDbContext db,
        IAuditTrail audit,
        IEventBus events,
        IRealtimeNotifier notifier,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var closure = await db.RoadClosures.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (closure is null) return Results.NotFound();

        var actor = ExtractActor(context);

        closure.RoadName = request.RoadName.Trim();
        closure.Description = request.Description.Trim();
        closure.Severity = request.Severity;
        closure.CoordinatesJson = string.IsNullOrWhiteSpace(request.CoordinatesJson) ? "[]" : request.CoordinatesJson.Trim();
        closure.BlockedReason = request.BlockedReason.Trim();
        closure.AlternateRouteAdvice = request.AlternateRouteAdvice?.Trim();
        closure.IsActive = request.IsActive;
        closure.EstimatedReopenUtc = request.EstimatedReopenUtc;

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(
            actor.Id,
            actor.Name,
            actor.Role,
            "RoadClosure.Update",
            "RoadClosure",
            closure.Id.ToString(),
            $"Updated road closure on \"{closure.RoadName}\" (Active: {closure.IsActive})",
            "Updated"), ct);

        await events.PublishAsync(new RoadClosureUpdated(
            closure.Id,
            closure.RoadName,
            closure.Severity,
            closure.IsActive), ct);

        await notifier.NotifyAllAsync(RealtimeTopics.RoadClosureUpdated, new
        {
            title = $"Road Closure Updated: {closure.RoadName}",
            isActive = closure.IsActive
        }, ct);

        return Json(new ApiEnvelope<RoadClosureDto>(SafetyZonesReadService.ToDto(closure)));
    }

    private static async Task<IResult> DeleteClosureAsync(
        Guid id,
        SafetyZonesDbContext db,
        IAuditTrail audit,
        IEventBus events,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var closure = await db.RoadClosures.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (closure is null) return Results.NotFound();

        var actor = ExtractActor(context);

        db.RoadClosures.Remove(closure);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(
            actor.Id,
            actor.Name,
            actor.Role,
            "RoadClosure.Delete",
            "RoadClosure",
            closure.Id.ToString(),
            $"Deleted road closure \"{closure.RoadName}\"",
            "Deleted"), ct);

        await events.PublishAsync(new RoadClosureUpdated(
            closure.Id,
            closure.RoadName,
            closure.Severity,
            false), ct);

        return Results.NoContent();
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static (Guid? Id, string Name, string Role) ExtractActor(HttpContext context)
    {
        var user = context.User;
        Guid? id = null;
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(idClaim, out var parsed))
        {
            id = parsed;
        }

        var name = user.Identity?.Name ?? "Government Operator";
        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? Roles.Government;
        return (id, name, role);
    }

    private static IResult Json<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        Results.Content(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), "application/json", Encoding.UTF8, statusCode);

    private static IResult DatabaseUnavailable() =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Database unavailable",
            detail: "The persistent store is unreachable. Operations requiring a database cannot be completed.");
}
