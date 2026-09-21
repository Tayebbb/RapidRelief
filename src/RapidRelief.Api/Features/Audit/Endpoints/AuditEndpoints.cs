using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Audit.Data;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Audit.Endpoints;

public static class AuditEndpoints
{
    public const string BasePath = "/api/audit";
    private const int MaxPageSize = 200;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization(AuthPolicies.RequireGovernment);

        group.MapGet("", ListAsync);
        group.MapGet("/actions", ActionsAsync);
    }

    private static async Task<IResult> ListAsync(
        AuditDbContext db,
        DatabaseHealth health,
        CancellationToken ct,
        string? action = null,
        string? entityType = null,
        string? entityId = null,
        Guid? actorId = null,
        string? q = null,
        int hours = 0,
        int page = 1,
        int pageSize = 50)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Entries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var wanted = action.Trim();
            query = query.Where(x => x.Action == wanted);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var wanted = entityType.Trim();
            query = query.Where(x => x.EntityType == wanted);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var wanted = entityId.Trim();
            query = query.Where(x => x.EntityId == wanted);
        }

        if (actorId is { } actor)
        {
            query = query.Where(x => x.ActorId == actor);
        }

        if (hours > 0)
        {
            var since = DateTimeOffset.UtcNow.AddHours(-Math.Min(hours, 24 * 90));
            query = query.Where(x => x.OccurredAtUtc >= since);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Summary.ToLower().Contains(term) ||
                x.ActorName.ToLower().Contains(term) ||
                x.Action.ToLower().Contains(term) ||
                x.EntityId.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(x => new AuditEntryDto(x.Id, x.ActorId, x.ActorName, x.ActorRole, x.Action,
                x.EntityType, x.EntityId, x.Summary, x.Result, x.Source, x.OccurredAtUtc))
            .ToList();

        return Results.Ok(new ApiEnvelope<PagedResult<AuditEntryDto>>(
            new PagedResult<AuditEntryDto>(items, page, pageSize, total)));
    }

    /// <summary>Populates the filter dropdowns from what is actually in the trail.</summary>
    private static async Task<IResult> ActionsAsync(AuditDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var actions = await db.Entries.AsNoTracking()
            .Select(x => x.Action).Distinct().OrderBy(x => x).ToListAsync(ct);
        var entityTypes = await db.Entries.AsNoTracking()
            .Select(x => x.EntityType).Distinct().OrderBy(x => x).ToListAsync(ct);

        return Results.Ok(new ApiEnvelope<AuditFacetsDto>(new AuditFacetsDto(actions, entityTypes)));
    }

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The audit trail is unavailable while the database is offline.");
}

public sealed record AuditFacetsDto(IReadOnlyList<string> Actions, IReadOnlyList<string> EntityTypes);
