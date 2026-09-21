using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;
using Severity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Features.Ai.Endpoints;

/// <summary>
/// /api/ai/assistant: any authenticated role (D-054), no-store on every response, and the
/// expensive POST on its own "assistant" budget while the cheap reads stay on "ai".
/// The POST must NEVER return 5xx — a degraded DB answers statelessly (§4.8).
/// </summary>
public static class AssistantEndpoints
{
    public const string BasePath = "/api/ai/assistant";

    /// <summary>Same assumption as AiEndpoints: the 50 nearest contain enough open shelters.</summary>
    private const int ShelterPrefetchCount = 50;

    /// <summary>How much of the operational picture a responder's answer may cite.</summary>
    private const int OperationalIncidentCount = 8;
    private const double OperationalRadiusKm = 25;

    private const string LoggerCategory = "RapidRelief.Api.Features.Ai.Endpoints.AssistantEndpoints";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization();
        group.AddEndpointFilter(AiEndpoints.CacheControlNoStoreFilter);

        group.MapPost("/messages", PostMessageAsync).RequireRateLimiting("assistant");
        group.MapGet("/sessions/{sessionId:guid}/messages", GetHistoryAsync).RequireRateLimiting("ai");
        group.MapDelete("/sessions/{sessionId:guid}", DeleteSessionAsync).RequireRateLimiting("ai");
    }

    private static async Task<IResult> PostMessageAsync(
        AssistantMessageRequest request,
        HttpContext httpContext,
        IValidator<AssistantMessageRequest> validator,
        IAssistantService assistant,
        AssistantOptions options,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        IShelterReadService shelters,
        IIncidentReadService incidents,
        IResponderAvailabilityService responders,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        if (!TryGetCaller(httpContext, out var userId))
        {
            return UnknownCaller();
        }

        var question = request.Message!.Trim();
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var degraded = databaseHealth.PostgresAvailable != true;
        var sessionId = request.SessionId ?? Guid.NewGuid();

        List<AssistantMessage> history = [];
        if (!degraded)
        {
            try
            {
                // Owner-scoped by construction (D-048): a forged session id yields an empty history,
                // never another user's chat. A session holds at most MaxSessionMessages rows, so
                // reading them all and windowing in memory is bounded and order-stable.
                history = await db.AssistantMessages.AsNoTracking()
                    .Where(m => m.UserId == userId && m.SessionId == sessionId)
                    .OrderBy(m => m.CreatedAtUtc)
                    .ThenBy(m => m.Id)
                    .ToListAsync(ct);

                if (history.Count >= options.MaxSessionMessages)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Conversation full",
                        detail: $"This conversation has reached its limit of {options.MaxSessionMessages} messages. Start a new chat.");
                }
            }
            catch (Exception ex) when (NotCallerCancellation(ex, ct))
            {
                // PostgresAvailable is only set at startup, so a database that died since then
                // arrives here. An unreadable history is a lost conversation, not a failed answer.
                logger.LogError(ex,
                    "Assistant history read failed for {UserId} in session {SessionId} — answering without it",
                    userId, sessionId);
                history = [];
            }
        }

        AssistantContext context;
        try
        {
            context = await BuildContextAsync(request, options, shelters, incidents, responders, httpContext, ct);
        }
        catch (Exception ex) when (NotCallerCancellation(ex, ct))
        {
            // Another module's read service is never allowed to fail this answer (§4.8).
            logger.LogError(ex, "Assistant context build failed for {UserId} — answering without shelters", userId);
            context = AssistantContext.None;
        }

        var turns = history
            .Select(m => new AssistantTurn(m.Role == AssistantRole.User, m.Text))
            .ToList();

        var answer = await assistant.AskAsync(new AssistantAsk(question, turns, context), ct);

        var now = timeProvider.GetUtcNow();
        if (degraded)
        {
            // §4.8 / D-005: the assistant answers statelessly rather than 503-ing.
            logger.LogInformation(
                "Assistant answered {UserId} while degraded — provider {Provider}, {LatencyMs} ms, not persisted",
                userId, answer.Provider, answer.LatencyMs);
            return Ok(new AssistantMessageResponse(null, ToDto(answer, now), Degraded: true, Persisted: false));
        }

        db.AssistantMessages.Add(new AssistantMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Role = AssistantRole.User,
            Text = question,
            Provider = null,
            CreatedAtUtc = now,
        });
        // One millisecond apart so history ordering survives both the SQLite ticks gate and
        // Npgsql's microsecond resolution — the answer genuinely follows the question.
        var answerAt = now.AddMilliseconds(1);
        db.AssistantMessages.Add(new AssistantMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            Role = AssistantRole.Model,
            Text = answer.Text,
            Provider = answer.Provider,
            CreatedAtUtc = answerAt,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (NotCallerCancellation(ex, ct))
        {
            // The answer already exists — deliver it, but never claim it was saved.
            logger.LogError(ex,
                "Assistant persist failed for {UserId} in session {SessionId} — answering unpersisted",
                userId, sessionId);
            return Ok(new AssistantMessageResponse(null, ToDto(answer, now), Degraded: true, Persisted: false));
        }

        logger.LogInformation(
            "Assistant answered {UserId} in session {SessionId} — provider {Provider}, {LatencyMs} ms, {Tokens} tokens, finish {FinishReason}",
            userId, sessionId, answer.Provider, answer.LatencyMs, answer.TokensUsed, answer.FinishReason);

        return Ok(new AssistantMessageResponse(sessionId, ToDto(answer, answerAt), Degraded: false, Persisted: true));
    }

    private static async Task<IResult> GetHistoryAsync(
        Guid sessionId,
        HttpContext httpContext,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        AssistantOptions options,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetCaller(httpContext, out var userId))
        {
            return UnknownCaller();
        }

        var messages = await db.AssistantMessages.AsNoTracking()
            .Where(m => m.UserId == userId && m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Take(options.MaxSessionMessages)
            .Select(m => new AssistantMessageDto(m.Id, m.Role.ToString(), m.Text, m.Provider, m.CreatedAtUtc))
            .ToListAsync(ct);

        return Ok(new AssistantHistoryResponse(sessionId, messages));
    }

    private static async Task<IResult> DeleteSessionAsync(
        Guid sessionId,
        HttpContext httpContext,
        AiDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetCaller(httpContext, out var userId))
        {
            return UnknownCaller();
        }

        await db.AssistantMessages
            .Where(m => m.UserId == userId && m.SessionId == sessionId)
            .ExecuteDeleteAsync(ct);

        return Results.NoContent();
    }

    /// <summary>
    /// D-052 context v1: nearest open shelters only, and only when the caller opted in by
    /// sending coordinates. The coordinates themselves never leave the machine. Responders and
    /// the command centre additionally get the operational picture their role already grants
    /// (D-102) — the role comes from the validated token, never from the request body.
    /// </summary>
    private static async Task<AssistantContext> BuildContextAsync(
        AssistantMessageRequest request,
        AssistantOptions options,
        IShelterReadService shelters,
        IIncidentReadService incidents,
        IResponderAvailabilityService responders,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var origin = request.Latitude is { } lat && request.Longitude is { } lng
            ? new GeoPoint(lat, lng)
            : (GeoPoint?)null;

        var shelterContext = Array.Empty<ShelterContext>() as IReadOnlyList<ShelterContext>;
        if (origin is { } point)
        {
            var nearest = await shelters.GetNearestAsync(point, ShelterPrefetchCount, ct);
            shelterContext = nearest
                .Where(s => s.IsOpen && s.Occupancy < s.Capacity)
                .Take(options.ShelterCount)
                .Select(s => new ShelterContext(s.Name,
                    Math.Round(GeoMath.HaversineMeters(point, s.Location) / 1000.0, 2),
                    s.Capacity - s.Occupancy))
                .ToList();
        }

        var role = ResolveRole(httpContext);
        var operations = role is Roles.Rescuer or Roles.Government
            ? await OperationalLinesAsync(role, origin, incidents, responders, ct)
            : [];

        return new AssistantContext(origin is not null, shelterContext, Array.Empty<string>(), role, operations);
    }

    /// <summary>Server-derived role only; a citizen never reaches the operational branch.</summary>
    private static string ResolveRole(HttpContext httpContext)
    {
        if (httpContext.User.IsInRole(Roles.Government))
        {
            return Roles.Government;
        }

        return httpContext.User.IsInRole(Roles.Rescuer) ? Roles.Rescuer : Roles.Citizen;
    }

    private static async Task<IReadOnlyList<string>> OperationalLinesAsync(
        string role,
        GeoPoint? origin,
        IIncidentReadService incidents,
        IResponderAvailabilityService responders,
        CancellationToken ct)
    {
        var lines = new List<string>();

        var query = origin is { } point
            ? new IncidentQuery(PageSize: OperationalIncidentCount, Near: point, RadiusKm: OperationalRadiusKm, OpenOnly: true)
            : new IncidentQuery(PageSize: OperationalIncidentCount, OpenOnly: true);
        var open = await incidents.GetIncidentsAsync(query, ct);

        var scope = origin is null ? "system-wide" : $"within {OperationalRadiusKm:F0} km";
        var critical = open.Items
            .Where(i => i.IsSos || i.Severity >= Severity.Severe)
            .Take(OperationalIncidentCount)
            .ToList();

        lines.Add($"Open incidents {scope}: {open.TotalCount}, of which {critical.Count} are critical or SOS.");

        foreach (var incident in critical)
        {
            var distance = origin is { } from
                ? $", {GeoMath.HaversineMeters(from, incident.Location) / 1000.0:F1} km away"
                : string.Empty;
            var priority = incident.PriorityScore is { } score ? $", AI priority {score:F0}/100" : string.Empty;
            var sos = incident.IsSos ? " (SOS)" : string.Empty;
            lines.Add($"{incident.Type} at severity {(int)incident.Severity}/5{sos}{distance}{priority}, "
                + $"status {incident.Status}: {incident.Summary}");
        }

        var capacity = await responders.GetAvailabilityAsync(origin, ct);
        lines.Add(capacity.TotalTeams == 0
            ? "Rescue capacity: no team registry data is available."
            : $"Rescue capacity: {capacity.AvailableTeams} of {capacity.TotalTeams} teams free, "
              + $"{capacity.DeployedTeams} deployed, {capacity.OpenMissions} missions open"
              + (capacity.NearestAvailableKm is { } km ? $", nearest free team {km:F1} km away." : "."));

        if (role == Roles.Government)
        {
            var byArea = open.Items
                .GroupBy(i => i.Type)
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => $"{g.Key} × {g.Count()}");
            lines.Add($"Open incidents by disaster type: {string.Join(", ", byArea)}.");
        }

        return lines;
    }

    private static AssistantAnswerDto ToDto(AssistantAnswer answer, DateTimeOffset createdAtUtc)
        => new(answer.Text, answer.Provider, answer.Truncated, createdAtUtc);

    private static IResult Ok<T>(T data) => Results.Ok(new ApiEnvelope<T>(data));

    private static bool TryGetCaller(HttpContext httpContext, out Guid userId)
        => Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    /// <summary>A client that hung up gets no answer; everything else degrades to one.</summary>
    private static bool NotCallerCancellation(Exception ex, CancellationToken ct)
        => ex is not OperationCanceledException || !ct.IsCancellationRequested;

    private static IResult UnknownCaller() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unknown caller",
        detail: "The access token carries no usable user id.");

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so conversation history is temporarily unavailable.");
}
