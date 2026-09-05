using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Incidents.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;
using Severity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Features.Incidents.Endpoints;

public static class IncidentsEndpoints
{
    public const string BasePath = "/api/incidents";
    private const int MaxPageSize = 100;
    private const long MaxMediaBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedMediaExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // D-011: ingestion is the abuse surface — the whole group carries the reports budget.
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .RequireRateLimiting("reports");

        group.MapPost("", CreateAsync);
        group.MapPost("/media", UploadMediaAsync).DisableAntiforgery();
        group.MapGet("", ListAsync);
        group.MapGet("/mine", MineAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/verify", VerifyAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapPost("/{id:guid}/resolve", ResolveAsync).RequireAuthorization(AuthPolicies.RequireGovernment);

        IncidentOpsEndpoints.Map(endpoints);
    }

    private static async Task<IResult> CreateAsync(
        CreateIncidentRequest request,
        IValidator<CreateIncidentRequest> validator,
        IncidentsDbContext db,
        IEventBus eventBus,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
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

        if (!TryGetUserId(context, out var reporterId))
        {
            return Results.Unauthorized();
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (idempotencyKey is not null)
        {
            var existing = await LoadAsync(db, x => x.ReporterId == reporterId && x.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
            {
                return Results.Ok(new ApiEnvelope<IncidentDto>(ToDto(existing)));
            }
        }

        var now = clock.GetUtcNow();
        var incident = new IncidentReport
        {
            Id = Guid.NewGuid(),
            ReporterId = reporterId,
            Title = request.Title!.Trim(),
            Description = request.Description!.Trim(),
            DisasterType = request.DisasterType,
            Severity = request.Severity,
            Status = IncidentStatus.Reported,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AddressOrArea = request.AddressOrArea?.Trim() ?? string.Empty,
            AffectedPeopleCount = request.AffectedPeopleCount,
            IsSos = request.IsSos,
            ContactPhone = request.ContactPhone?.Trim() ?? string.Empty,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        var photoPaths = (request.PhotoPaths ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Take(5)
            .ToList();

        foreach (var path in photoPaths)
        {
            incident.Media.Add(new IncidentMedia
            {
                IncidentId = incident.Id,
                FileUrl = path,
                MediaType = ContentTypeFor(path),
                UploadedAtUtc = now,
            });
        }

        incident.StatusHistory.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.Reported,
            ToStatus = IncidentStatus.Reported,
            ChangedByUserId = reporterId,
            Notes = request.IsSos ? "SOS report received" : "Report received",
            ChangedAtUtc = now,
        });

        db.Reports.Add(incident);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // Concurrent replay of the same key — return the row that won the race.
            db.ChangeTracker.Clear();
            var winner = await LoadAsync(db, x => x.ReporterId == reporterId && x.IdempotencyKey == idempotencyKey, ct);
            if (winner is null)
            {
                throw;
            }

            return Results.Ok(new ApiEnvelope<IncidentDto>(ToDto(winner)));
        }

        // The AI pipeline (F8) starts here: handler → bounded channel → worker → IncidentAssessed.
        await eventBus.PublishAsync(
            new IncidentCreated(incident.Id, reporterId, incident.DisasterType, incident.Severity,
                new GeoPoint(incident.Latitude, incident.Longitude), incident.Description, incident.IsSos,
                photoPaths, incident.AffectedPeopleCount),
            ct);

        loggerFactory.CreateLogger(typeof(IncidentsEndpoints)).LogInformation(
            "Incident {IncidentId} reported (type {Type}, sos {IsSos})", incident.Id, incident.DisasterType, incident.IsSos);

        return Results.Created($"{BasePath}/{incident.Id}", new ApiEnvelope<IncidentDto>(ToDto(incident)));
    }

    private static async Task<IResult> UploadMediaAsync(
        IFormFile? file,
        IFileStorage storage,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["A file is required."] });
        }

        if (file.Length > MaxMediaBytes)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Photos must be 10 MB or smaller."] });
        }

        var fileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedMediaExtensions.Contains(extension))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [$"Only {string.Join(", ", AllowedMediaExtensions)} photos are allowed."],
            });
        }

        await using var content = file.OpenReadStream();
        var stored = await storage.SaveAsync(content, fileName, ContentTypeFor(fileName), ct);
        return Results.Ok(new ApiEnvelope<UploadedMediaDto>(
            new UploadedMediaDto(stored.Path, stored.Url, stored.SizeBytes, stored.ContentType)));
    }

    private static async Task<IResult> ListAsync(
        IncidentsDbContext db,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct,
        IncidentStatus? status = null,
        DisasterType? type = null,
        Severity? severity = null,
        bool? sos = null,
        string? q = null,
        bool? unassigned = null,
        int page = 1,
        int pageSize = 50)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        // Citizens only ever see their own reports through this route; responders see the feed.
        var isResponder = context.User.IsInRole(Roles.Rescuer) || context.User.IsInRole(Roles.Government);
        if (!isResponder)
        {
            return await MineAsync(db, health, context, ct, page, pageSize);
        }

        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Reports.AsNoTracking().AsQueryable();
        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }

        if (type is { } wantedType)
        {
            query = query.Where(x => x.DisasterType == wantedType);
        }

        if (severity is { } wantedSeverity)
        {
            query = query.Where(x => x.Severity == wantedSeverity);
        }

        if (sos == true)
        {
            query = query.Where(x => x.IsSos);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Title.ToLower().Contains(term) ||
                x.Description.ToLower().Contains(term) ||
                x.AddressOrArea.ToLower().Contains(term));
        }

        if (unassigned == true)
        {
            query = query.Where(x => x.AssignedMissionId == null
                && x.Status != IncidentStatus.Resolved
                && x.Status != IncidentStatus.Rejected);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.IsSos)
            .ThenByDescending(x => x.PriorityScore ?? 0)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(x => x.Media)
            .Include(x => x.StatusHistory)
            .ToListAsync(ct);

        // Contact details are for the command centre only. Rescuers get the reporter's phone from
        // the incident they are actually assigned (GET /{id}), not by paging the whole feed.
        var includeContact = context.User.IsInRole(Roles.Government);

        return Results.Ok(new ApiEnvelope<PagedResult<IncidentDto>>(
            new PagedResult<IncidentDto>(rows.Select(x => ToDto(x, includeContact)).ToList(), page, pageSize, total)));
    }

    private static async Task<IResult> MineAsync(
        IncidentsDbContext db,
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

        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Reports.AsNoTracking().Where(x => x.ReporterId == userId);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(x => x.Media)
            .Include(x => x.StatusHistory)
            .ToListAsync(ct);

        return Results.Ok(new ApiEnvelope<PagedResult<IncidentDto>>(
            new PagedResult<IncidentDto>(rows.Select(x => ToDto(x, includeContact: true)).ToList(), page, pageSize, total)));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IncidentsDbContext db,
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

        var incident = await LoadAsync(db, x => x.Id == id, ct);
        if (incident is null)
        {
            return Results.NotFound();
        }

        // A citizen may read only their own report — no cross-citizen data leakage.
        var isResponder = context.User.IsInRole(Roles.Rescuer) || context.User.IsInRole(Roles.Government);
        if (!isResponder && incident.ReporterId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ApiEnvelope<IncidentDto>(ToDto(incident, includeContact: true)));
    }

    private static async Task<IResult> VerifyAsync(
        Guid id,
        VerifyIncidentRequest request,
        IValidator<VerifyIncidentRequest> validator,
        IncidentsDbContext db,
        IEventBus eventBus,
        IRealtimeNotifier notifier,
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

        if (!TryGetUserId(context, out var officerId))
        {
            return Results.Unauthorized();
        }

        var incident = await LoadTrackedAsync(db, id, ct);
        if (incident is null)
        {
            return Results.NotFound();
        }

        if (incident.Status is not (IncidentStatus.Reported or IncidentStatus.Verified))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Incident already in progress",
                detail: $"A report in status {incident.Status} can no longer be verified or rejected.");
        }

        var now = clock.GetUtcNow();
        var target = request.Approved ? IncidentStatus.Verified : IncidentStatus.Rejected;
        AppendStatus(db, incident, target, officerId,
            request.Approved ? "Verified by command centre" : $"Rejected: {request.Reason}", now);

        incident.VerifiedByGovernmentId = officerId;
        incident.VerifiedAtUtc = now;
        incident.RejectionReason = request.Approved ? null : request.Reason;
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(new IncidentVerified(incident.Id, officerId, request.Approved, request.Reason), ct);
        await NotifyReporterAsync(notifier, incident,
            request.Approved
                ? "Your report was verified — responders are being assigned."
                : $"Your report was closed by the command centre: {request.Reason}",
            ct);

        return Results.Ok(new ApiEnvelope<IncidentDto>(ToDto(incident)));
    }

    /// <summary>
    /// Command-centre close-out for incidents that never became a mission (false alarm handled,
    /// resolved by another agency). Anything with a live mission must be closed from rescue ops,
    /// otherwise the incident and its mission would disagree about what happened.
    /// </summary>
    private static async Task<IResult> ResolveAsync(
        Guid id,
        ResolveIncidentRequest request,
        IValidator<ResolveIncidentRequest> validator,
        IncidentsDbContext db,
        IRealtimeNotifier notifier,
        IAuditTrail audit,
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

        if (!TryGetUserId(context, out var officerId))
        {
            return Results.Unauthorized();
        }

        var incident = await LoadTrackedAsync(db, id, ct);
        if (incident is null)
        {
            return Results.NotFound();
        }

        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Rejected)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Incident already closed",
                detail: $"This report is already {incident.Status}.");
        }

        if (incident.AssignedMissionId is not null && incident.MissionStage is not ("Completed" or "Cancelled"))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Mission still active",
                detail: "Complete or cancel the rescue mission before closing this incident.");
        }

        var now = clock.GetUtcNow();
        AppendStatus(db, incident, IncidentStatus.Resolved, officerId,
            $"Closed by command centre: {request.Notes}", now);
        await db.SaveChangesAsync(ct);

        await NotifyReporterAsync(notifier, incident,
            "Your report has been closed by the command centre.", ct);
        await audit.RecordAsync(new AuditRecord(officerId, string.Empty, Roles.Government,
            "Incident.Resolve", "Incident", incident.Id.ToString(),
            $"Closed without a mission: {request.Notes}", "Resolved"), ct);

        return Results.Ok(new ApiEnvelope<IncidentDto>(ToDto(incident, includeContact: true)));
    }

    /// <summary>
    /// Adds the history row through the DbSet: the entity assigns its own key, so attaching it via
    /// the tracked parent's collection would make EF treat it as an existing (Modified) row.
    /// </summary>
    internal static void AppendStatus(
        IncidentsDbContext db,
        IncidentReport incident,
        IncidentStatus target,
        Guid actorId,
        string notes,
        DateTimeOffset now)
    {
        db.StatusHistory.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = incident.Status,
            ToStatus = target,
            ChangedByUserId = actorId,
            Notes = notes,
            ChangedAtUtc = now,
        });

        incident.Status = target;
        incident.UpdatedAtUtc = now;
        if (target == IncidentStatus.Resolved)
        {
            incident.ResolvedAtUtc = now;
        }
    }

    internal static Task NotifyReporterAsync(IRealtimeNotifier notifier, IncidentReport incident, string message, CancellationToken ct)
        => notifier.NotifyUserAsync(incident.ReporterId, Topics.IncidentStatus, new
        {
            title = message,
            incidentId = incident.Id,
            status = incident.Status.ToString(),
        }, ct);

    private static Task<IncidentReport?> LoadAsync(
        IncidentsDbContext db,
        System.Linq.Expressions.Expression<Func<IncidentReport, bool>> predicate,
        CancellationToken ct)
        => db.Reports.AsNoTracking()
            .Include(x => x.Media)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(predicate, ct);

    private static Task<IncidentReport?> LoadTrackedAsync(IncidentsDbContext db, Guid id, CancellationToken ct)
        => db.Reports.Include(x => x.Media).Include(x => x.StatusHistory).FirstOrDefaultAsync(x => x.Id == id, ct);

    internal static bool TryGetUserId(HttpContext context, out Guid userId)
        => Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    internal static IncidentDto ToDto(IncidentReport report, bool includeContact = false) => new(
        report.Id,
        report.ReporterId,
        report.Title,
        report.Description,
        report.DisasterType,
        report.Severity,
        report.Status,
        new GeoPoint(report.Latitude, report.Longitude),
        report.AddressOrArea,
        report.AffectedPeopleCount,
        report.IsSos,
        includeContact ? report.ContactPhone : null,
        report.PriorityScore,
        report.AiSummary,
        report.PossibleDuplicateOfId,
        report.AssignedTeamId,
        report.AssignedMissionId,
        report.MissionStage,
        report.RejectionReason,
        report.CreatedAtUtc,
        report.UpdatedAtUtc,
        report.ResolvedAtUtc,
        report.Media.OrderBy(m => m.UploadedAtUtc)
            .Select(m => new IncidentMediaDto(m.Id, m.FileUrl, m.MediaType, m.FileSizeBytes, m.UploadedAtUtc)).ToList(),
        report.StatusHistory.OrderBy(h => h.ChangedAtUtc)
            .Select(h => new IncidentStatusEntryDto(h.FromStatus, h.ToStatus, h.Notes, h.ChangedAtUtc)).ToList());

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    internal static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): incident data is temporarily unavailable.");
}

/// <summary>D-036 topic names owned by this slice — the strings live in the shared registry.</summary>
public static class Topics
{
    public const string IncidentStatus = RealtimeTopics.IncidentStatus;
    public const string IncidentReported = RealtimeTopics.IncidentReported;
}
