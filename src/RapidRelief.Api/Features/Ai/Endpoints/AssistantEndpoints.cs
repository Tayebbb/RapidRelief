using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Services;

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
            context = await BuildContextAsync(request, options, shelters, ct);
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
    /// sending coordinates. The coordinates themselves never leave the machine.
    /// </summary>
    private static async Task<AssistantContext> BuildContextAsync(
        AssistantMessageRequest request, AssistantOptions options, IShelterReadService shelters,
        CancellationToken ct)
    {
        if (request.Latitude is not { } latitude || request.Longitude is not { } longitude)
        {
            return AssistantContext.None;
        }

        var origin = new GeoPoint(latitude, longitude);
        var nearest = await shelters.GetNearestAsync(origin, ShelterPrefetchCount, ct);
        var candidates = nearest
            .Where(s => s.IsOpen && s.Occupancy < s.Capacity)
            .Take(options.ShelterCount)
            .Select(s => new ShelterContext(s.Name,
                Math.Round(GeoMath.HaversineMeters(origin, s.Location) / 1000.0, 2),
                s.Capacity - s.Occupancy))
            .ToList();

        return new AssistantContext(HasLocation: true, candidates, Array.Empty<string>());
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
