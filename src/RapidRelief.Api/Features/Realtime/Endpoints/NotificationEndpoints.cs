using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Features.Realtime.Pipeline;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Features.Realtime.Endpoints;

// Feature-local wire records (D-019 precedent) — Shared/Contracts is untouched by F9.

public sealed record NotificationDto(
    Guid Id,
    string Topic,
    string Summary,
    string PayloadJson,
    string Audience,
    string? Role,
    Guid? UserId,
    DateTimeOffset CreatedAtUtc,
    bool IsRead);

public sealed record NotificationPage(
    IReadOnlyList<NotificationDto> Items,
    DateTimeOffset ServerTimeUtc,
    string? NextCursor);

public sealed record MarkedResponse(int Marked);

public sealed record UnreadCountResponse(int Count);

/// <summary>
/// /api/realtime/notifications: any authenticated role, "realtime" rate-limit policy,
/// no-store on every response. Audience filtering is server-side only — a caller can never
/// see another user's or another role's rows.
/// </summary>
public static class NotificationEndpoints
{
    public const string BasePath = "/api/realtime/notifications";
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxReadAllPerCall = 1000;

    /// <summary>Ceiling on the rows read for one timestamp tick (see <see cref="AfterCursorAsync"/>).</summary>
    private const int TickReadCap = MaxLimit + 1;

    private const string LoggerCategory = "RapidRelief.Api.Features.Realtime.Endpoints.NotificationEndpoints";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .RequireRateLimiting("realtime");
        group.AddEndpointFilter(CacheControlNoStoreFilter);

        group.MapGet("", GetAsync);
        group.MapPatch("/{id:guid}/read", MarkReadAsync);
        group.MapPost("/read-all", MarkAllReadAsync);
        group.MapGet("/unread-count", GetUnreadCountAsync);
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        NotificationsDbContext db,
        DatabaseHealth databaseHealth,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        string? since,
        int? limit,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetCaller(httpContext, out var userId, out var roles))
        {
            return UnknownCaller();
        }

        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var pageSize = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var visible = VisibleTo(db, userId, roles);

        List<Notification> rows;
        if (since is null)
        {
            rows = await NewestAsync(visible, pageSize, logger, ct);
        }
        else
        {
            if (!NotificationCursor.TryDecode(since, out var cursorTime, out var cursorId))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid cursor",
                    detail: "The 'since' value is not a cursor issued by this endpoint.");
            }

            rows = await AfterCursorAsync(visible, cursorTime, cursorId, pageSize, logger, ct);
        }

        var ids = rows.Select(n => n.Id).ToList();
        var readIds = await db.Reads.AsNoTracking()
            .Where(r => r.UserId == userId && ids.Contains(r.NotificationId))
            .Select(r => r.NotificationId)
            .ToListAsync(ct);

        var items = rows
            .Select(n => new NotificationDto(n.Id, n.Topic, n.Summary, n.PayloadJson, n.Audience, n.Role,
                n.UserId, n.CreatedAtUtc, readIds.Contains(n.Id)))
            .ToList();
        var nextCursor = rows.Count > 0
            ? NotificationCursor.Encode(rows[^1].CreatedAtUtc, rows[^1].Id)
            : since;

        return Results.Ok(new ApiEnvelope<NotificationPage>(
            new NotificationPage(items, timeProvider.GetUtcNow(), nextCursor)));
    }

    private static async Task<IResult> MarkReadAsync(
        Guid id,
        HttpContext httpContext,
        NotificationsDbContext db,
        DatabaseHealth databaseHealth,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetCaller(httpContext, out var userId, out var roles))
        {
            return UnknownCaller();
        }

        // Visibility first: an invisible id must look exactly like a missing one.
        if (!await VisibleTo(db, userId, roles).AnyAsync(n => n.Id == id, ct))
        {
            return NotificationNotFound();
        }

        if (await db.Reads.AnyAsync(r => r.NotificationId == id && r.UserId == userId, ct))
        {
            return Results.NoContent();
        }

        db.Reads.Add(new NotificationRead
        {
            NotificationId = id,
            UserId = userId,
            ReadAtUtc = timeProvider.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent mark-read on the same row: the composite PK already holds the truth.
            db.ChangeTracker.Clear();
        }

        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllReadAsync(
        HttpContext httpContext,
        NotificationsDbContext db,
        DatabaseHealth databaseHealth,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetCaller(httpContext, out var userId, out var roles))
        {
            return UnknownCaller();
        }

        var marked = await MarkAllVisibleReadAsync(
            db, userId, roles, timeProvider.GetUtcNow(), loggerFactory.CreateLogger(LoggerCategory), ct);

        return Results.Ok(new ApiEnvelope<MarkedResponse>(new MarkedResponse(marked)));
    }

    /// <summary>
    /// Marks every visible unread row read and returns how many THIS call claimed. A concurrent
    /// mark-read collides on the composite key and fails the whole batch, so recovery is one
    /// re-query plus one batch retry — never a per-row loop over up to <see cref="MaxReadAllPerCall"/> ids.
    /// </summary>
    public static async Task<int> MarkAllVisibleReadAsync(
        NotificationsDbContext db,
        Guid userId,
        List<string> roles,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken ct)
    {
        var unreadIds = await UnreadIdsAsync(db, userId, roles, ct);
        AddReads(db, unreadIds, userId, now);
        try
        {
            await db.SaveChangesAsync(ct);
            return unreadIds.Count;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }

        var stillUnread = await UnreadIdsAsync(db, userId, roles, ct);
        logger.LogInformation(
            "Read-all for {UserId} collided with a concurrent read — retrying {Remaining} of {Attempted} rows",
            userId, stillUnread.Count, unreadIds.Count);
        if (stillUnread.Count == 0)
        {
            return 0;
        }

        AddReads(db, stillUnread, userId, now);
        try
        {
            await db.SaveChangesAsync(ct);
            return stillUnread.Count;
        }
        catch (DbUpdateException ex)
        {
            // Two collisions in a row means another tab is marking the same rows; the read
            // state it wrote is just as valid, so report nothing claimed instead of failing.
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Read-all for {UserId} lost the retry as well — reporting 0 marked", userId);
            return 0;
        }
    }

    private static Task<List<Guid>> UnreadIdsAsync(
        NotificationsDbContext db, Guid userId, List<string> roles, CancellationToken ct)
        => VisibleTo(db, userId, roles)
            .Where(n => !db.Reads.Any(r => r.NotificationId == n.Id && r.UserId == userId))
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => n.Id)
            .Take(MaxReadAllPerCall)
            .ToListAsync(ct);

    private static void AddReads(NotificationsDbContext db, List<Guid> ids, Guid userId, DateTimeOffset now)
    {
        foreach (var id in ids)
        {
            db.Reads.Add(new NotificationRead { NotificationId = id, UserId = userId, ReadAtUtc = now });
        }
    }

    private static async Task<IResult> GetUnreadCountAsync(
        HttpContext httpContext,
        NotificationsDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetCaller(httpContext, out var userId, out var roles))
        {
            return UnknownCaller();
        }

        // D-039: plain COUNT over exactly the rows the inbox lists — no denormalised counter and
        // no extra time window, or the badge and the list would disagree. The retention sweep
        // (D-034) is what bounds the table.
        var count = await VisibleTo(db, userId, roles)
            .Where(n => !db.Reads.Any(r => r.NotificationId == n.Id && r.UserId == userId))
            .CountAsync(ct);

        return Results.Ok(new ApiEnvelope<UnreadCountResponse>(new UnreadCountResponse(count)));
    }

    private static IQueryable<Notification> VisibleTo(NotificationsDbContext db, Guid userId, List<string> roles)
        => db.Notifications.AsNoTracking().Where(n =>
            n.Audience == NotificationAudience.All ||
            (n.Audience == NotificationAudience.Role && n.Role != null && roles.Contains(n.Role)) ||
            (n.Audience == NotificationAudience.User && n.UserId == userId));

    /// <summary>
    /// Newest page, returned oldest-first. The boundary tick is fetched in full so the
    /// (CreatedAtUtc, Id) order is identical on every call regardless of provider tie-breaking.
    /// </summary>
    private static async Task<List<Notification>> NewestAsync(
        IQueryable<Notification> visible, int limit, ILogger logger, CancellationToken ct)
    {
        var page = await visible.OrderByDescending(n => n.CreatedAtUtc).Take(limit + 1).ToListAsync(ct);
        var merged = await MergeBoundaryTickAsync(visible, page, logger, ct);
        return merged.Count <= limit ? merged : merged[^limit..];
    }

    /// <summary>
    /// Keyset step (D-038). The cursor's own tick is read in full and filtered in memory, then
    /// the remainder comes from strictly later ticks — so rows sharing a tick can never be
    /// skipped by provider-specific tie-breaking. Both tick reads are capped: a pathological
    /// same-tick burst must not become an unbounded materialisation.
    /// </summary>
    private static async Task<List<Notification>> AfterCursorAsync(
        IQueryable<Notification> visible, DateTimeOffset cursorTime, Guid cursorId, int limit,
        ILogger logger, CancellationToken ct)
    {
        var sameTick = await visible.Where(n => n.CreatedAtUtc == cursorTime).Take(TickReadCap).ToListAsync(ct);
        WarnIfTickCapped(logger, sameTick.Count, cursorTime);
        sameTick.Sort(CompareOrder);
        var rows = sameTick.Where(n => IsAfterCursor(n, cursorTime, cursorId)).Take(limit).ToList();
        if (rows.Count == limit)
        {
            return rows;
        }

        var remaining = limit - rows.Count;
        var page = await visible
            .Where(n => n.CreatedAtUtc > cursorTime)
            .OrderBy(n => n.CreatedAtUtc)
            .Take(remaining + 1)
            .ToListAsync(ct);
        rows.AddRange((await MergeBoundaryTickAsync(visible, page, logger, ct)).Take(remaining));
        return rows;
    }

    private static async Task<List<Notification>> MergeBoundaryTickAsync(
        IQueryable<Notification> visible, List<Notification> page, ILogger logger, CancellationToken ct)
    {
        if (page.Count > 0)
        {
            var boundary = page[^1].CreatedAtUtc;
            var ties = await visible.Where(n => n.CreatedAtUtc == boundary).Take(TickReadCap).ToListAsync(ct);
            WarnIfTickCapped(logger, ties.Count, boundary);
            page = page.UnionBy(ties, n => n.Id).ToList();
        }

        page.Sort(CompareOrder);
        return page;
    }

    private static void WarnIfTickCapped(ILogger logger, int read, DateTimeOffset tick)
    {
        if (read == TickReadCap)
        {
            logger.LogWarning(
                "More than {Cap} notifications share tick {Tick:o} — the page was capped and cursor paging may stall",
                TickReadCap, tick);
        }
    }

    private static int CompareOrder(Notification left, Notification right)
        => left.CreatedAtUtc != right.CreatedAtUtc
            ? left.CreatedAtUtc.CompareTo(right.CreatedAtUtc)
            : string.CompareOrdinal(left.Id.ToString("D"), right.Id.ToString("D"));

    private static bool IsAfterCursor(Notification notification, DateTimeOffset cursorTime, Guid cursorId)
        => notification.CreatedAtUtc > cursorTime ||
           (notification.CreatedAtUtc == cursorTime &&
            string.CompareOrdinal(notification.Id.ToString("D"), cursorId.ToString("D")) > 0);

    private static bool TryGetCaller(HttpContext httpContext, out Guid userId, out List<string> roles)
    {
        roles = httpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static IResult UnknownCaller() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unknown caller",
        detail: "The access token carries no usable user id.");

    private static IResult NotificationNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Notification not found",
        detail: "No notification with that id is visible to you.");

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so database-backed endpoints are temporarily unavailable.");

    internal static async ValueTask<object?> CacheControlNoStoreFilter(
        EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        invocationContext.HttpContext.Response.Headers.CacheControl = "no-store, private";
        return await next(invocationContext);
    }
}
