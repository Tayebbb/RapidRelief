using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai.Endpoints;

public sealed record DuplicateFlagDto(
    Guid IncidentId,
    Guid PossibleDuplicateOfId,
    double Confidence,
    string Reason,
    string? Decision,
    DateTimeOffset FlaggedAtUtc,
    DateTimeOffset? ReviewedAtUtc);

public sealed record DuplicateDecisionRequest(string? Note);

/// <summary>
/// Decision-support surface: the full structured insight behind an incident, and the duplicate
/// queue an operator reviews. Nothing here deletes or merges a report — flags are advisory and a
/// human records the verdict.
/// </summary>
public static class AiInsightEndpoints
{
    public const string BasePath = "/api/ai/insights";
    public const string DuplicatesPath = "/api/ai/duplicates";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var insights = endpoints.MapGroup(BasePath)
            // Responder-only for the same reason as the assessment routes: the insight is derived
            // from the reporter's free text and is keyed only by incident id. A citizen sees their
            // own AI summary through the incident DTO, which is already owner-scoped.
            .RequireAuthorization(AuthPolicies.RequireResponder)
            .RequireRateLimiting("ai");
        insights.AddEndpointFilter(AiEndpoints.CacheControlNoStoreFilter);
        insights.MapGet("/{incidentId:guid}", GetInsightAsync);

        var duplicates = endpoints.MapGroup(DuplicatesPath)
            .RequireAuthorization(AuthPolicies.RequireResponder)
            .RequireRateLimiting("ai");
        duplicates.AddEndpointFilter(AiEndpoints.CacheControlNoStoreFilter);
        duplicates.MapGet("", ListDuplicatesAsync);
        duplicates.MapPost("/{incidentId:guid}/confirm", ConfirmAsync)
            .RequireAuthorization(AuthPolicies.RequireGovernment);
        duplicates.MapPost("/{incidentId:guid}/dismiss", DismissAsync)
            .RequireAuthorization(AuthPolicies.RequireGovernment);
    }

    private static async Task<IResult> GetInsightAsync(
        Guid incidentId,
        AiDbContext db,
        DatabaseHealth health,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var row = await db.Assessments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.IncidentId == incidentId, ct);
        if (row is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Assessment not found",
                detail: "The incident has not been assessed yet.");
        }

        return Results.Ok(new ApiEnvelope<AiInsightDto>(ToInsight(row)));
    }

    private static async Task<IResult> ListDuplicatesAsync(
        AiDbContext db,
        DatabaseHealth health,
        CancellationToken ct,
        bool pendingOnly = true,
        int take = 50)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var query = db.Assessments.AsNoTracking().Where(a => a.PossibleDuplicateOfId != null);
        if (pendingOnly)
        {
            query = query.Where(a => a.DuplicateDecision == null);
        }

        var rows = await query
            .OrderByDescending(a => a.DuplicateConfidence ?? 0)
            .ThenByDescending(a => a.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

        var items = rows.Select(a => new DuplicateFlagDto(
            a.IncidentId, a.PossibleDuplicateOfId!.Value, a.DuplicateConfidence ?? 0,
            a.DuplicateReason ?? string.Empty, a.DuplicateDecision, a.CreatedAtUtc,
            a.DuplicateReviewedAtUtc)).ToList();

        return Results.Ok(new ApiEnvelope<IReadOnlyList<DuplicateFlagDto>>(items));
    }

    private static Task<IResult> ConfirmAsync(
        Guid incidentId, DuplicateDecisionRequest request, AiDbContext db, DatabaseHealth health,
        IAuditTrail audit, HttpContext context, TimeProvider clock, CancellationToken ct)
        => DecideAsync(incidentId, request, "Confirmed", db, health, audit, context, clock, ct);

    private static Task<IResult> DismissAsync(
        Guid incidentId, DuplicateDecisionRequest request, AiDbContext db, DatabaseHealth health,
        IAuditTrail audit, HttpContext context, TimeProvider clock, CancellationToken ct)
        => DecideAsync(incidentId, request, "Dismissed", db, health, audit, context, clock, ct);

    /// <summary>
    /// Records the operator's verdict on the flag. Confirming does NOT close the incident — that
    /// stays with the incident owner, so a wrongly confirmed flag can never silently drop a real
    /// emergency out of the queue.
    /// </summary>
    private static async Task<IResult> DecideAsync(
        Guid incidentId,
        DuplicateDecisionRequest request,
        string decision,
        AiDbContext db,
        DatabaseHealth health,
        IAuditTrail audit,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var row = await db.Assessments.FirstOrDefaultAsync(a => a.IncidentId == incidentId, ct);
        if (row is null || row.PossibleDuplicateOfId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                title: "No duplicate flag",
                detail: "This incident is not flagged as a possible duplicate.");
        }

        if (row.DuplicateDecision is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Already reviewed",
                detail: $"This flag was already {row.DuplicateDecision.ToLowerInvariant()}.");
        }

        row.DuplicateDecision = decision;
        row.DuplicateReviewedAtUtc = clock.GetUtcNow();
        row.DuplicateReviewedByUserId = AiEndpoints.CallerId(context);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(row.DuplicateReviewedByUserId, string.Empty, string.Empty,
            $"Duplicate.{decision}", "Incident", incidentId.ToString(),
            $"Flag against {row.PossibleDuplicateOfId} {decision.ToLowerInvariant()}"
            + (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $": {request.Note.Trim()}"),
            decision), ct);

        return Results.Ok(new ApiEnvelope<DuplicateFlagDto>(new DuplicateFlagDto(
            row.IncidentId, row.PossibleDuplicateOfId.Value, row.DuplicateConfidence ?? 0,
            row.DuplicateReason ?? string.Empty, row.DuplicateDecision, row.CreatedAtUtc,
            row.DuplicateReviewedAtUtc)));
    }

    internal static AiInsightDto ToInsight(AiAssessment row) => new(
        row.IncidentId,
        row.PredictedType,
        row.EstimatedSeverity,
        row.Confidence,
        string.IsNullOrWhiteSpace(row.Urgency) ? AiUrgency.Standard : row.Urgency,
        row.EstimatedPeopleAffected,
        row.MedicalUrgency,
        Deserialize<List<string>>(row.DamageIndicatorsJson) ?? [],
        row.Summary,
        Explanation(row),
        row.PriorityScore,
        string.IsNullOrWhiteSpace(row.PriorityBand) ? "Medium" : row.PriorityBand,
        Deserialize<List<AiPriorityFactorDto>>(row.PriorityFactorsJson) ?? [],
        row.Provider,
        row.ModelName,
        row.PossibleDuplicateOfId,
        row.DuplicateConfidence,
        row.DuplicateReason,
        row.CreatedAtUtc);

    /// <summary>A degraded run states so plainly — silently downgrading confidence would mislead.</summary>
    private static string Explanation(AiAssessment row)
        => string.IsNullOrWhiteSpace(row.DegradedReason)
            ? row.Reasoning
            : $"{row.Reasoning} ({row.DegradedReason} — assessed by the offline rule engine.)";

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            // A malformed stored blob must degrade to "no detail", never fail the read.
            return null;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "AI insights are unavailable while the database is offline.");
}
