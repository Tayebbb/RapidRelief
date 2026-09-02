using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Alerts.Data;
using RapidRelief.Api.Features.Alerts.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Features.Alerts.Endpoints;

public static class AlertsEndpoints
{
    public const string BasePath = "/api/alerts";
    private const int MaxPage = 1_000_000;
    private const int MaxPageSize = 100;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireRateLimiting("alerts");
        group.MapPost("", CreateAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapGet("", ListAsync).AllowAnonymous();
        group.MapGet("/active", ActiveAsync).AllowAnonymous();
        group.MapGet("/{id:guid}", GetAsync).AllowAnonymous();
        group.MapPost("/{id:guid}/revoke", RevokeAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
    }

    private static async Task<IResult> CreateAsync(
        CreateAlertRequest request,
        IValidator<CreateAlertRequest> validator,
        AlertsDbContext db,
        IEventBus eventBus,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var authorId))
        {
            return Results.Unauthorized();
        }

        var now = clock.GetUtcNow();
        if (request.ExpiresAtUtc <= now)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ExpiresAtUtc)] = ["ExpiresAtUtc must be in the future."] });
        }

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            AuthorGovernmentUserId = authorId,
            Title = request.Title!.Trim(),
            Body = request.Body!.Trim(),
            Severity = request.Severity,
            DisasterType = request.DisasterType,
            TargetArea = request.TargetArea!.Trim(),
            RadiusKm = request.RadiusKm,
            ExpiresAtUtc = request.ExpiresAtUtc.ToUniversalTime(),
            CreatedAtUtc = now,
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(new AlertPublished(alert.Id, alert.Title, alert.Body,
            alert.Severity, alert.DisasterType, alert.ExpiresAtUtc), ct);

        return Json(new ApiEnvelope<AlertDto>(ToDto(alert)), StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(
        AlertsDbContext db,
        DatabaseHealth health,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        page = Math.Clamp(page, 1, MaxPage);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = db.Alerts.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc);
        var total = await query.CountAsync(ct);
        var alerts = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Json(new ApiEnvelope<PagedResult<AlertDto>>(
            new PagedResult<AlertDto>(alerts.Select(ToDto).ToList(), page, pageSize, total)));
    }

    private static async Task<IResult> ActiveAsync(AlertsDbContext db, DatabaseHealth health, TimeProvider clock, CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var now = clock.GetUtcNow();
        var alerts = await db.Alerts.AsNoTracking()
            .Where(x => x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Json(new ApiEnvelope<List<AlertDto>>(alerts.Select(ToDto).ToList()));
    }

    private static async Task<IResult> GetAsync(Guid id, AlertsDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var alert = await db.Alerts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return alert is null ? Results.NotFound() : Json(new ApiEnvelope<AlertDto>(ToDto(alert)));
    }

    private static async Task<IResult> RevokeAsync(Guid id, AlertsDbContext db, DatabaseHealth health, TimeProvider clock, CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var alert = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (alert is null)
        {
            return Results.NotFound();
        }

        if (alert.RevokedAtUtc is null)
        {
            alert.RevokedAtUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static AlertDto ToDto(Alert alert) => new(alert.Id, alert.Title, alert.Body, alert.Severity,
        alert.DisasterType, alert.TargetArea, alert.RadiusKm, alert.ExpiresAtUtc, alert.CreatedAtUtc, alert.RevokedAtUtc);

    private static IResult Json<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        Results.Content(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), "application/json", Encoding.UTF8, statusCode);

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): alert data is temporarily unavailable.");
}
