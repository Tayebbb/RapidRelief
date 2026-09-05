using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Relief.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Relief.Endpoints;

public static class ReliefEndpoints
{
    public const string BasePath = "/api/relief";

    /// <summary>Citizen-visible topic (D-036) — one notification per meaningful transition, no spam.</summary>
    public const string StatusTopic = RealtimeTopics.ReliefStatus;

    private const int MaxPageSize = 100;

    /// <summary>
    /// Requested → Accepted → Preparing → Dispatched → Delivered, with rejection available until
    /// the goods leave the warehouse. Anything else is a 409.
    /// </summary>
    private static readonly Dictionary<ReliefStatus, ReliefStatus[]> AllowedTransitions = new()
    {
        [ReliefStatus.Pending] = [ReliefStatus.Approved, ReliefStatus.Rejected],
        [ReliefStatus.Approved] = [ReliefStatus.Allocated, ReliefStatus.Rejected],
        [ReliefStatus.Allocated] = [ReliefStatus.Dispatched, ReliefStatus.Rejected],
        [ReliefStatus.Dispatched] = [ReliefStatus.Delivered],
        [ReliefStatus.Delivered] = [],
        [ReliefStatus.Rejected] = [],
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .RequireRateLimiting("reports");

        group.MapPost("/requests", CreateAsync);
        group.MapGet("/requests/mine", MineAsync);
        group.MapGet("/requests", QueueAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapGet("/requests/{id:guid}", GetAsync);
        group.MapPost("/requests/{id:guid}/status", UpdateStatusAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapPost("/requests/{id:guid}/cancel", CancelAsync);

        ReliefResourceEndpoints.Map(endpoints);
    }

    private static async Task<IResult> CreateAsync(
        CreateReliefRequest request,
        IValidator<CreateReliefRequest> validator,
        ReliefDbContext db,
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

        if (!TryGetUserId(context, out var requesterId))
        {
            return Results.Unauthorized();
        }

        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key is not null)
        {
            var existing = await db.Requests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.RequesterId == requesterId && x.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                return Results.Ok(new ApiEnvelope<ReliefRequestDto>(ToDto(existing)));
            }
        }

        var now = clock.GetUtcNow();
        var entity = new ReliefRequest
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            IncidentId = request.IncidentId,
            ReliefType = request.Type.ToString(),
            UrgencyLevel = string.IsNullOrWhiteSpace(request.Urgency) ? "High" : request.Urgency!.Trim(),
            QuantityRequested = request.Quantity,
            RecipientCount = request.RecipientCount,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DeliveryAddress = request.DeliveryAddress?.Trim() ?? string.Empty,
            Notes = request.Notes?.Trim() ?? string.Empty,
            Status = ReliefStatus.Pending,
            IdempotencyKey = key,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Requests.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (key is not null)
        {
            db.ChangeTracker.Clear();
            var winner = await db.Requests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.RequesterId == requesterId && x.IdempotencyKey == key, ct);
            if (winner is null)
            {
                throw;
            }

            return Results.Ok(new ApiEnvelope<ReliefRequestDto>(ToDto(winner)));
        }

        await eventBus.PublishAsync(new ReliefRequested(entity.Id, requesterId, request.Type,
            entity.QuantityRequested, new GeoPoint(entity.Latitude, entity.Longitude), UrgencyLevel(entity.UrgencyLevel)), ct);

        return Results.Created($"{BasePath}/requests/{entity.Id}", new ApiEnvelope<ReliefRequestDto>(ToDto(entity)));
    }

    private static async Task<IResult> MineAsync(
        ReliefDbContext db,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        return await PageAsync(db.Requests.AsNoTracking().Where(x => x.RequesterId == userId), page, pageSize, ct);
    }

    private static async Task<IResult> QueueAsync(
        ReliefDbContext db,
        DatabaseHealth health,
        CancellationToken ct,
        ReliefStatus? status = null,
        bool openOnly = false,
        int page = 1,
        int pageSize = 50)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var query = db.Requests.AsNoTracking().AsQueryable();
        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }
        else if (openOnly)
        {
            query = query.Where(x => x.Status != ReliefStatus.Delivered && x.Status != ReliefStatus.Rejected);
        }

        return await PageAsync(query, page, pageSize, ct);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ReliefDbContext db,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var entity = await db.Requests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return Results.NotFound();
        }

        // A citizen may only read their own request — no cross-household data leakage.
        if (!context.User.IsInRole(Roles.Government) && entity.RequesterId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ApiEnvelope<ReliefRequestDto>(ToDto(entity)));
    }

    private static async Task<IResult> UpdateStatusAsync(
        Guid id,
        UpdateReliefStatusRequest request,
        IValidator<UpdateReliefStatusRequest> validator,
        ReliefDbContext db,
        IEventBus eventBus,
        IRealtimeNotifier notifier,
        DatabaseHealth health,
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

        var entity = await db.Requests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return Results.NotFound();
        }

        if (!AllowedTransitions.TryGetValue(entity.Status, out var allowed) || !allowed.Contains(request.Status))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Invalid relief transition",
                detail: $"A request in status {entity.Status} cannot move to {request.Status}.");
        }

        entity.Status = request.Status;
        entity.UpdatedAtUtc = clock.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            entity.Notes = string.IsNullOrWhiteSpace(entity.Notes)
                ? request.Note!.Trim()
                : $"{entity.Notes}\n{request.Note!.Trim()}";
        }

        await db.SaveChangesAsync(ct);
        await eventBus.PublishAsync(new ReliefStatusChanged(entity.Id, entity.Status), ct);
        await NotifyRequesterAsync(notifier, entity, ct);

        return Results.Ok(new ApiEnvelope<ReliefRequestDto>(ToDto(entity)));
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        ReliefDbContext db,
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

        if (!TryGetUserId(context, out var userId))
        {
            return Results.Unauthorized();
        }

        var entity = await db.Requests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null || entity.RequesterId != userId)
        {
            return Results.NotFound();
        }

        if (entity.Status is not (ReliefStatus.Pending or ReliefStatus.Approved))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Too late to cancel",
                detail: "Supplies are already being prepared for this request.");
        }

        entity.Status = ReliefStatus.Rejected;
        entity.Notes = string.IsNullOrWhiteSpace(entity.Notes) ? "Cancelled by requester" : $"{entity.Notes}\nCancelled by requester";
        entity.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await eventBus.PublishAsync(new ReliefStatusChanged(entity.Id, entity.Status), ct);

        return Results.Ok(new ApiEnvelope<ReliefRequestDto>(ToDto(entity)));
    }

    /// <summary>Only transitions a citizen can act on produce a notification (no spam).</summary>
    private static Task NotifyRequesterAsync(IRealtimeNotifier notifier, ReliefRequest entity, CancellationToken ct)
    {
        var message = entity.Status switch
        {
            ReliefStatus.Approved => $"Your {entity.ReliefType.ToLowerInvariant()} request was accepted.",
            ReliefStatus.Allocated => $"Your {entity.ReliefType.ToLowerInvariant()} supplies are being prepared.",
            ReliefStatus.Dispatched => $"Your {entity.ReliefType.ToLowerInvariant()} supplies are on the way.",
            ReliefStatus.Delivered => $"Your {entity.ReliefType.ToLowerInvariant()} supplies were delivered.",
            ReliefStatus.Rejected => $"Your {entity.ReliefType.ToLowerInvariant()} request could not be fulfilled.",
            _ => null,
        };

        return message is null
            ? Task.CompletedTask
            : notifier.NotifyUserAsync(entity.RequesterId, StatusTopic, new
            {
                title = message,
                requestId = entity.Id,
                status = entity.Status.ToString(),
            }, ct);
    }

    private static async Task<IResult> PageAsync(IQueryable<ReliefRequest> query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Results.Ok(new ApiEnvelope<PagedResult<ReliefRequestDto>>(
            new PagedResult<ReliefRequestDto>(rows.Select(ToDto).ToList(), page, pageSize, total)));
    }

    private static int UrgencyLevel(string urgency) => urgency.ToLowerInvariant() switch
    {
        "critical" => 5,
        "high" => 4,
        "medium" => 3,
        "low" => 2,
        _ => 3,
    };

    private static bool TryGetUserId(HttpContext context, out Guid userId)
        => Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static ReliefRequestDto ToDto(ReliefRequest entity) => new(
        entity.Id,
        entity.RequesterId,
        Enum.TryParse<ResourceType>(entity.ReliefType, ignoreCase: true, out var type) ? type : ResourceType.Other,
        entity.QuantityRequested,
        entity.RecipientCount,
        entity.UrgencyLevel,
        entity.Status,
        new GeoPoint(entity.Latitude, entity.Longitude),
        entity.DeliveryAddress,
        entity.Notes,
        entity.IncidentId,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc);

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): relief data is temporarily unavailable.");
}
