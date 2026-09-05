using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Features.Rescue.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Rescue.Endpoints;

public static class RescueEndpoints
{
    public const string BasePath = "/api/rescue";

    /// <summary>Pushed to the assigned team's members (D-036).</summary>
    public const string MissionTopic = RealtimeTopics.RescueMissionAssigned;

    /// <summary>Pushed to responders when a mission or a team changes state.</summary>
    public const string OperationsTopic = RealtimeTopics.RescueOperations;

    /// <summary>Pushed when a mission moves along its lifecycle.</summary>
    public const string MissionStatusTopic = RealtimeTopics.RescueMissionStatus;

    /// <summary>Pushed when a team's availability changes, so the map can restyle it.</summary>
    public const string TeamTopic = RealtimeTopics.RescueTeamAvailability;

    private const int MaxPageSize = 100;
    private const int NearbyRadiusKm = 10;

    /// <summary>Only these transitions are legal; anything else is a 409.</summary>
    private static readonly Dictionary<MissionStatus, MissionStatus[]> AllowedTransitions = new()
    {
        [MissionStatus.Assigned] = [MissionStatus.EnRoute, MissionStatus.Cancelled],
        [MissionStatus.EnRoute] = [MissionStatus.OnScene, MissionStatus.Cancelled],
        [MissionStatus.OnScene] = [MissionStatus.Completed, MissionStatus.Cancelled],
        [MissionStatus.Completed] = [],
        [MissionStatus.Cancelled] = [],
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization(AuthPolicies.RequireResponder);

        group.MapGet("/dashboard", DashboardAsync);
        group.MapGet("/queue", QueueAsync);
        group.MapGet("/missions", MissionsAsync);
        group.MapGet("/missions/{id:guid}", MissionAsync);
        group.MapPost("/missions", AssignAsync);
        group.MapPost("/missions/{id:guid}/accept", AcceptAsync);
        group.MapPost("/missions/{id:guid}/reject", RejectAsync);
        group.MapPost("/missions/{id:guid}/status", UpdateStatusAsync);
        group.MapPost("/missions/{id:guid}/reassign", ReassignAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapGet("/teams", TeamsAsync);
        group.MapGet("/teams/suitable", SuitableTeamsAsync);
        group.MapPost("/teams", CreateTeamAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapPut("/teams/{id:guid}", UpdateTeamAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapPost("/teams/mine/position", UpdatePositionAsync).RequireAuthorization(AuthPolicies.RequireRescuer);
        group.MapPost("/teams/mine/status", UpdateTeamStatusAsync).RequireAuthorization(AuthPolicies.RequireRescuer);
    }

    // ── Dashboard ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> DashboardAsync(
        RescueDbContext db,
        IIncidentReadService incidents,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct,
        double? lat = null,
        double? lng = null)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var team = await FindMemberTeamAsync(db, actorId, ct);
        var origin = ResolveOrigin(lat, lng, team);

        var open = await OpenQueueAsync(incidents, origin, ct);
        var byBand = open.GroupBy(x => x.Band).ToDictionary(g => g.Key, g => g.Count());
        foreach (var band in new[] { "Critical", "High", "Medium", "Low" })
        {
            byBand.TryAdd(band, 0);
        }

        var critical = open.Where(x => x.Band == "Critical").Take(5).ToList();
        var nearby = origin is null
            ? []
            : open.Where(x => x.DistanceKm is { } d && d <= NearbyRadiusKm)
                .OrderBy(x => x.DistanceKm)
                .Take(5)
                .ToList();

        var teamId = team?.Id;
        var missions = teamId is null
            ? []
            : await db.Missions.AsNoTracking().Where(x => x.AssignedTeamId == teamId).ToListAsync(ct);

        var activeCount = await db.Missions.AsNoTracking()
            .CountAsync(x => x.Status != MissionStatus.Completed && x.Status != MissionStatus.Cancelled, ct);

        return Results.Ok(new ApiEnvelope<RescueDashboardDto>(new RescueDashboardDto(
            byBand,
            critical,
            nearby,
            missions.Count(x => x.Status == MissionStatus.Assigned),
            missions.Count(x => x.Status is MissionStatus.EnRoute or MissionStatus.OnScene),
            missions.Count(x => x.Status == MissionStatus.Completed),
            team is null ? null : ToTeamDto(team, activeCount: missions.Count(x =>
                x.Status != MissionStatus.Completed && x.Status != MissionStatus.Cancelled)))));
    }

    private static async Task<IResult> QueueAsync(
        RescueDbContext db,
        IIncidentReadService incidents,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct,
        string? band = null,
        double? lat = null,
        double? lng = null,
        int page = 1,
        int pageSize = 25)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        TryGetUserId(context, out var actorId);
        var team = await FindMemberTeamAsync(db, actorId, ct);
        var origin = ResolveOrigin(lat, lng, team);

        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var open = await OpenQueueAsync(incidents, origin, ct);
        if (!string.IsNullOrWhiteSpace(band))
        {
            open = open.Where(x => string.Equals(x.Band, band, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var items = open.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Results.Ok(new ApiEnvelope<PagedResult<QueueItemDto>>(
            new PagedResult<QueueItemDto>(items, page, pageSize, open.Count)));
    }

    /// <summary>
    /// Verified work first; unverified reports stay visible behind it so the queue never stalls
    /// while the command centre is busy. SOS and AI priority always float to the top.
    /// </summary>
    private static async Task<List<QueueItemDto>> OpenQueueAsync(
        IIncidentReadService incidents,
        GeoPoint? origin,
        CancellationToken ct)
    {
        var verified = await incidents.GetIncidentsAsync(new IncidentQuery(IncidentStatus.Verified, PageSize: 100), ct);
        var reported = await incidents.GetIncidentsAsync(new IncidentQuery(IncidentStatus.Reported, PageSize: 100), ct);

        return verified.Items.Concat(reported.Items)
            .Select(x => new QueueItemDto(
                x.Id, x.Type, x.Severity, x.Status, x.Location, x.Summary, x.IsSos, x.PriorityScore, x.ReportedAtUtc,
                TeamSuitabilityScorer.Band(x),
                origin is null ? null : Math.Round(TeamSuitabilityScorer.Haversine(origin, x.Location), 2)))
            .OrderByDescending(x => x.IsSos)
            .ThenByDescending(x => x.PriorityScore ?? 0)
            .ThenByDescending(x => x.ReportedAtUtc)
            .ToList();
    }

    // ── Assignment ─────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> SuitableTeamsAsync(
        Guid incidentId,
        RescueDbContext db,
        IIncidentReadService incidents,
        DatabaseHealth health,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var incident = await incidents.GetByIdAsync(incidentId, ct);
        if (incident is null)
        {
            return Results.NotFound();
        }

        var teams = await db.Teams.AsNoTracking().Include(x => x.Members).ToListAsync(ct);
        var active = await ActiveMissionCountsAsync(db, ct);

        var ranked = TeamSuitabilityScorer.Rank(incident.Location, incident.Type, teams, active)
            .Select(x => new TeamSuitabilityDto(
                x.Team.Id, x.Team.TeamName, x.Team.Specialization, x.Team.Status,
                x.DistanceKm is { } d ? Math.Round(d, 2) : null, x.ActiveMissions, x.Reasons))
            .ToList();

        return Results.Ok(new ApiEnvelope<List<TeamSuitabilityDto>>(ranked));
    }

    private static async Task<IResult> AssignAsync(
        AssignMissionRequest request,
        IValidator<AssignMissionRequest> validator,
        RescueDbContext db,
        IIncidentReadService incidents,
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

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var incident = await incidents.GetByIdAsync(request.IncidentId, ct);
        if (incident is null)
        {
            return Results.NotFound();
        }

        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Rejected)
        {
            return Conflict("Incident closed", $"Incident {request.IncidentId} is {incident.Status} and cannot be assigned.");
        }

        if (await HasActiveMissionAsync(db, request.IncidentId, ct))
        {
            return Conflict("Already assigned", "This incident already has an active mission.");
        }

        var team = await ResolveTeamAsync(db, request.TeamId, actorId, context, clock, ct);
        if (team is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TeamId)] = ["Unknown team. Government users must supply an existing TeamId."],
            });
        }

        if (string.Equals(team.Status, TeamStatus.OffDuty, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict("Team off duty", $"{team.TeamName} is off duty and cannot be dispatched.");
        }

        if (await TeamHasActiveMissionAsync(db, team.Id, ct))
        {
            return Conflict("Team already deployed", $"{team.TeamName} is already running a mission.");
        }

        var now = clock.GetUtcNow();
        var mission = NewMission(request, incident, team, actorId, now);
        db.Missions.Add(mission);
        team.Status = TeamStatus.Dispatched;
        team.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(new MissionAssigned(mission.Id, mission.IncidentId, team.Id, actorId), ct);
        await NotifyTeamAsync(db, notifier, team, mission, incident, ct);
        await NotifyOperationsAsync(notifier, $"{team.TeamName} dispatched to a {incident.Type} incident", mission, ct);

        return Results.Created($"{BasePath}/missions/{mission.Id}", new ApiEnvelope<RescueMissionDto>(ToDto(mission, team.TeamName)));
    }

    private static async Task<IResult> AcceptAsync(
        Guid id,
        RescueDbContext db,
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

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var mission = await db.Missions.Include(x => x.Logs).Include(x => x.Team)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (mission is null)
        {
            return Results.NotFound();
        }

        if (!await CanDriveAsync(db, context, mission, actorId, ct))
        {
            return Results.Forbid();
        }

        if (mission.Status != MissionStatus.Assigned)
        {
            return Conflict("Nothing to accept", $"This mission is already {mission.Status}.");
        }

        var now = clock.GetUtcNow();
        mission.AcceptedAtUtc ??= now;
        mission.UpdatedAtUtc = now;
        AppendLog(db, mission, actorId, "Accepted", "Dispatch acknowledged by the team", now);
        await db.SaveChangesAsync(ct);

        await NotifyOperationsAsync(notifier, $"{mission.Team?.TeamName} accepted the mission", mission, ct);
        return Results.Ok(new ApiEnvelope<RescueMissionDto>(ToDto(mission, mission.Team?.TeamName ?? string.Empty)));
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        RejectMissionRequest request,
        IValidator<RejectMissionRequest> validator,
        RescueDbContext db,
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

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var mission = await db.Missions.Include(x => x.Logs).Include(x => x.Team)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (mission is null)
        {
            return Results.NotFound();
        }

        if (!await CanDriveAsync(db, context, mission, actorId, ct))
        {
            return Results.Forbid();
        }

        // A team can only hand back work it has not started — after that it must be cancelled.
        if (mission.Status != MissionStatus.Assigned)
        {
            return Conflict("Too late to reject", $"This mission is already {mission.Status}; cancel it instead.");
        }

        var now = clock.GetUtcNow();
        CloseMission(db, mission, MissionStatus.Cancelled, actorId, $"Rejected: {request.Reason}", now);
        mission.RejectionReason = request.Reason;
        await db.SaveChangesAsync(ct);

        // Cancelling returns the incident to the queue via the Incidents projection.
        await eventBus.PublishAsync(new MissionStatusChanged(mission.Id, mission.IncidentId, MissionStatus.Cancelled), ct);
        await NotifyOperationsAsync(notifier, $"{mission.Team?.TeamName} could not take the mission — back in the queue", mission, ct);

        return Results.Ok(new ApiEnvelope<RescueMissionDto>(ToDto(mission, mission.Team?.TeamName ?? string.Empty)));
    }

    private static async Task<IResult> ReassignAsync(
        Guid id,
        ReassignMissionRequest request,
        IValidator<ReassignMissionRequest> validator,
        RescueDbContext db,
        IIncidentReadService incidents,
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

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var mission = await db.Missions.Include(x => x.Logs).Include(x => x.Team)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (mission is null)
        {
            return Results.NotFound();
        }

        if (mission.Status is MissionStatus.Completed or MissionStatus.Cancelled)
        {
            return Conflict("Mission closed", $"A {mission.Status} mission cannot be reassigned.");
        }

        var target = await db.Teams.FirstOrDefaultAsync(x => x.Id == request.TeamId, ct);
        if (target is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TeamId)] = ["Unknown team."],
            });
        }

        if (target.Id == mission.AssignedTeamId)
        {
            return Conflict("Same team", "The mission is already assigned to that team.");
        }

        if (string.Equals(target.Status, TeamStatus.OffDuty, StringComparison.OrdinalIgnoreCase) ||
            await TeamHasActiveMissionAsync(db, target.Id, ct))
        {
            return Conflict("Team unavailable", $"{target.TeamName} cannot take another mission right now.");
        }

        var incident = await incidents.GetByIdAsync(mission.IncidentId, ct);
        if (incident is null)
        {
            return Results.NotFound();
        }

        var now = clock.GetUtcNow();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Reassigned by the command centre" : request.Reason!.Trim();
        CloseMission(db, mission, MissionStatus.Cancelled, actorId, reason, now);

        var replacement = NewMission(
            new AssignMissionRequest(mission.IncidentId, target.Id, mission.MissionTitle, mission.Priority),
            incident, target, actorId, now);
        db.Missions.Add(replacement);
        target.Status = TeamStatus.Dispatched;
        target.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        // The incident follows the NEW mission: assignment is published after the cancellation.
        await eventBus.PublishAsync(new MissionStatusChanged(mission.Id, mission.IncidentId, MissionStatus.Cancelled), ct);
        await eventBus.PublishAsync(new MissionAssigned(replacement.Id, replacement.IncidentId, target.Id, actorId), ct);
        await NotifyTeamAsync(db, notifier, target, replacement, incident, ct);
        await NotifyOperationsAsync(notifier, $"Mission reassigned to {target.TeamName}", replacement, ct);

        return Results.Ok(new ApiEnvelope<RescueMissionDto>(ToDto(replacement, target.TeamName)));
    }

    // ── Mission state machine ──────────────────────────────────────────────────────────────────

    private static async Task<IResult> UpdateStatusAsync(
        Guid id,
        UpdateMissionStatusRequest request,
        IValidator<UpdateMissionStatusRequest> validator,
        RescueDbContext db,
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

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var mission = await db.Missions.Include(x => x.Logs).Include(x => x.Team)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (mission is null)
        {
            return Results.NotFound();
        }

        if (!await CanDriveAsync(db, context, mission, actorId, ct))
        {
            return Results.Forbid();
        }

        if (!AllowedTransitions.TryGetValue(mission.Status, out var allowed) || !allowed.Contains(request.Status))
        {
            return Conflict("Invalid mission transition", $"A mission in status {mission.Status} cannot move to {request.Status}.");
        }

        var now = clock.GetUtcNow();
        mission.Status = request.Status;
        mission.UpdatedAtUtc = now;
        mission.AcceptedAtUtc ??= now;

        switch (request.Status)
        {
            case MissionStatus.EnRoute:
                mission.StartedAtUtc ??= now;
                break;
            case MissionStatus.OnScene:
                mission.OnSceneAtUtc ??= now;
                break;
            case MissionStatus.Completed:
            case MissionStatus.Cancelled:
                CloseMission(db, mission, request.Status, actorId, request.Notes ?? string.Empty, now);
                break;
        }

        if (request.Status is not (MissionStatus.Completed or MissionStatus.Cancelled))
        {
            AppendLog(db, mission, actorId, request.Status.ToString(), request.Notes?.Trim() ?? string.Empty, now);
        }

        await db.SaveChangesAsync(ct);
        await eventBus.PublishAsync(new MissionStatusChanged(mission.Id, mission.IncidentId, mission.Status), ct);
        await NotifyOperationsAsync(notifier, $"{mission.Team?.TeamName} — mission {request.Status}", mission, ct,
            MissionStatusTopic);

        return Results.Ok(new ApiEnvelope<RescueMissionDto>(ToDto(mission, mission.Team?.TeamName ?? string.Empty)));
    }

    private static async Task<IResult> MissionsAsync(
        RescueDbContext db,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct,
        bool mine = false,
        bool activeOnly = false,
        Guid? incidentId = null,
        int page = 1,
        int pageSize = 50)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Missions.AsNoTracking().Include(x => x.Logs).Include(x => x.Team).AsQueryable();
        if (mine)
        {
            var teamIds = await db.Teams.AsNoTracking()
                .Where(t => t.TeamLeadUserId == actorId || t.Members.Any(m => m.RescuerUserId == actorId))
                .Select(t => t.Id)
                .ToListAsync(ct);
            query = query.Where(x => teamIds.Contains(x.AssignedTeamId));
        }

        if (incidentId is { } wanted)
        {
            query = query.Where(x => x.IncidentId == wanted);
        }

        if (activeOnly)
        {
            query = query.Where(x => x.Status != MissionStatus.Completed && x.Status != MissionStatus.Cancelled);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.AssignedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Results.Ok(new ApiEnvelope<PagedResult<RescueMissionDto>>(
            new PagedResult<RescueMissionDto>(
                rows.Select(x => ToDto(x, x.Team?.TeamName ?? string.Empty)).ToList(), page, pageSize, total)));
    }

    private static async Task<IResult> MissionAsync(Guid id, RescueDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var mission = await db.Missions.AsNoTracking().Include(x => x.Logs).Include(x => x.Team)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return mission is null
            ? Results.NotFound()
            : Results.Ok(new ApiEnvelope<RescueMissionDto>(ToDto(mission, mission.Team?.TeamName ?? string.Empty)));
    }

    // ── Teams ──────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> TeamsAsync(RescueDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var active = await ActiveMissionCountsAsync(db, ct);
        var teams = await db.Teams.AsNoTracking().ToListAsync(ct);

        return Results.Ok(new ApiEnvelope<List<RescueTeamDto>>(
            teams.OrderBy(t => t.TeamName)
                .Select(t => ToTeamDto(t, active.TryGetValue(t.Id, out var count) ? count : 0))
                .ToList()));
    }

    private static async Task<IResult> CreateTeamAsync(
        CreateTeamRequest request,
        IValidator<CreateTeamRequest> validator,
        RescueDbContext db,
        IAuditTrail audit,
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

        var now = clock.GetUtcNow();
        var team = new RescueTeam
        {
            Id = Guid.NewGuid(),
            TeamName = request.TeamName!.Trim(),
            Specialization = string.IsNullOrWhiteSpace(request.Specialization) ? "General" : request.Specialization!.Trim(),
            ContactNumber = request.ContactNumber?.Trim() ?? string.Empty,
            TeamLeadUserId = request.TeamLeadUserId ?? Guid.Empty,
            Status = TeamStatus.Available,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        if (request.TeamLeadUserId is { } leadId && leadId != Guid.Empty)
        {
            team.Members.Add(new RescueTeamMember { TeamId = team.Id, RescuerUserId = leadId, JoinedAtUtc = now });
        }

        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            "Team.Create", "RescueTeam", team.Id.ToString(),
            $"Created team \"{team.TeamName}\" ({team.Specialization})", "Created"), ct);

        return Results.Created($"{BasePath}/teams/{team.Id}", new ApiEnvelope<RescueTeamDto>(ToTeamDto(team, 0)));
    }

    /// <summary>Government team registry edit. Duty status is refused mid-mission, exactly as the rescuer's own toggle is.</summary>
    private static async Task<IResult> UpdateTeamAsync(
        Guid id,
        UpdateTeamRequest request,
        IValidator<UpdateTeamRequest> validator,
        RescueDbContext db,
        IAuditTrail audit,
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

        var team = await db.Teams.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (team is null)
        {
            return Results.NotFound();
        }

        var wantedStatus = string.IsNullOrWhiteSpace(request.Status) ? team.Status : request.Status!.Trim();
        if (!TeamStatus.IsKnown(wantedStatus))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown team status",
                detail: $"Status must be one of {TeamStatus.Available}, {TeamStatus.Dispatched} or {TeamStatus.OffDuty}.");
        }

        if (wantedStatus != team.Status && team.Status == TeamStatus.Dispatched)
        {
            var live = await db.Missions.AnyAsync(m => m.AssignedTeamId == id
                && m.Status != MissionStatus.Completed && m.Status != MissionStatus.Cancelled, ct);
            if (live)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Team is on a mission",
                    detail: "Complete, cancel or reassign the team's mission before changing its duty status.");
            }
        }

        var before = $"{team.TeamName} · {team.Specialization} · {team.Status}";
        team.TeamName = request.TeamName!.Trim();
        team.Specialization = string.IsNullOrWhiteSpace(request.Specialization) ? team.Specialization : request.Specialization!.Trim();
        team.ContactNumber = request.ContactNumber?.Trim() ?? string.Empty;
        team.Status = wantedStatus;
        team.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            "Team.Update", "RescueTeam", team.Id.ToString(),
            $"{before} → {team.TeamName} · {team.Specialization} · {team.Status}", "Updated"), ct);

        var active = await ActiveMissionCountsAsync(db, ct);
        return Results.Ok(new ApiEnvelope<RescueTeamDto>(
            ToTeamDto(team, active.TryGetValue(team.Id, out var count) ? count : 0)));
    }

    private static async Task<IResult> UpdatePositionAsync(
        TeamPositionRequest request,
        IValidator<TeamPositionRequest> validator,
        RescueDbContext db,
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

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var team = await FindMemberTeamAsync(db, actorId, ct);
        if (team is null)
        {
            return Results.NotFound();
        }

        team.CurrentLatitude = request.Latitude;
        team.CurrentLongitude = request.Longitude;
        if (!string.IsNullOrWhiteSpace(request.Status) && TeamStatus.IsKnown(request.Status!))
        {
            team.Status = request.Status!;
        }

        team.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> UpdateTeamStatusAsync(
        TeamStatusRequest request,
        IValidator<TeamStatusRequest> validator,
        RescueDbContext db,
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

        if (!TryGetUserId(context, out var actorId))
        {
            return Results.Unauthorized();
        }

        var team = await FindMemberTeamAsync(db, actorId, ct);
        if (team is null)
        {
            return Results.NotFound();
        }

        // A deployed team cannot declare itself free while a mission is still open.
        if (!string.Equals(request.Status, TeamStatus.Dispatched, StringComparison.OrdinalIgnoreCase) &&
            await TeamHasActiveMissionAsync(db, team.Id, ct))
        {
            return Conflict("Mission in progress", "Close or hand over the active mission before changing status.");
        }

        team.Status = request.Status!;
        team.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await notifier.NotifyRoleAsync(Roles.Government, TeamTopic, new
        {
            title = $"{team.TeamName} is now {team.Status}",
            teamId = team.Id,
            status = team.Status,
        }, ct);

        return Results.Ok(new ApiEnvelope<RescueTeamDto>(ToTeamDto(team, 0)));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static RescueMission NewMission(
        AssignMissionRequest request,
        IncidentSummaryDto incident,
        RescueTeam team,
        Guid actorId,
        DateTimeOffset now)
    {
        var mission = new RescueMission
        {
            Id = Guid.NewGuid(),
            IncidentId = request.IncidentId,
            AssignedTeamId = team.Id,
            MissionTitle = string.IsNullOrWhiteSpace(request.MissionTitle) ? $"{incident.Type} response" : request.MissionTitle!.Trim(),
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? (incident.IsSos ? "Critical" : "Urgent") : request.Priority!.Trim(),
            Status = MissionStatus.Assigned,
            AssignedByUserId = actorId,
            AssignedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        mission.Logs.Add(new RescueMissionLog
        {
            MissionId = mission.Id,
            LoggedByUserId = actorId,
            StatusUpdate = MissionStatus.Assigned.ToString(),
            Message = $"Assigned to {team.TeamName}",
            TimestampUtc = now,
        });

        return mission;
    }

    private static void CloseMission(
        RescueDbContext db,
        RescueMission mission,
        MissionStatus status,
        Guid actorId,
        string notes,
        DateTimeOffset now)
    {
        mission.Status = status;
        mission.CompletedAtUtc = now;
        mission.UpdatedAtUtc = now;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            mission.OutcomeNotes = notes;
        }

        if (mission.Team is not null)
        {
            // Never strand a team in "Dispatched" — off-duty teams stay off duty.
            if (!string.Equals(mission.Team.Status, TeamStatus.OffDuty, StringComparison.OrdinalIgnoreCase))
            {
                mission.Team.Status = TeamStatus.Available;
            }

            mission.Team.UpdatedAtUtc = now;
        }

        AppendLog(db, mission, actorId, status.ToString(), notes, now);
    }

    private static void AppendLog(
        RescueDbContext db,
        RescueMission mission,
        Guid actorId,
        string statusUpdate,
        string message,
        DateTimeOffset now)
    {
        // Added through the DbSet: the log assigns its own key, so attaching it via the tracked
        // mission's collection would make EF treat it as an existing (Modified) row.
        db.MissionLogs.Add(new RescueMissionLog
        {
            MissionId = mission.Id,
            LoggedByUserId = actorId,
            StatusUpdate = statusUpdate,
            Message = message,
            TimestampUtc = now,
        });
    }

    private static Task<bool> HasActiveMissionAsync(RescueDbContext db, Guid incidentId, CancellationToken ct)
        => db.Missions.AsNoTracking().AnyAsync(
            x => x.IncidentId == incidentId && x.Status != MissionStatus.Cancelled && x.Status != MissionStatus.Completed, ct);

    private static Task<bool> TeamHasActiveMissionAsync(RescueDbContext db, Guid teamId, CancellationToken ct)
        => db.Missions.AsNoTracking().AnyAsync(
            x => x.AssignedTeamId == teamId && x.Status != MissionStatus.Cancelled && x.Status != MissionStatus.Completed, ct);

    private static async Task<Dictionary<Guid, int>> ActiveMissionCountsAsync(RescueDbContext db, CancellationToken ct)
        => await db.Missions.AsNoTracking()
            .Where(x => x.Status != MissionStatus.Completed && x.Status != MissionStatus.Cancelled)
            .GroupBy(x => x.AssignedTeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, ct);

    private static async Task<bool> CanDriveAsync(
        RescueDbContext db,
        HttpContext context,
        RescueMission mission,
        Guid actorId,
        CancellationToken ct)
        => context.User.IsInRole(Roles.Government) ||
           await db.Teams.AsNoTracking().AnyAsync(
               t => t.Id == mission.AssignedTeamId &&
                    (t.TeamLeadUserId == actorId || t.Members.Any(m => m.RescuerUserId == actorId)), ct);

    /// <summary>
    /// A rescuer without a registered team gets a personal one on first assignment — the demo must
    /// never dead-end on missing seed data, and the row is a real team the government can rename.
    /// </summary>
    private static async Task<RescueTeam?> ResolveTeamAsync(
        RescueDbContext db,
        Guid? requestedTeamId,
        Guid actorId,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (requestedTeamId is { } teamId && teamId != Guid.Empty)
        {
            // Only the command centre dispatches a team it is not a member of. Without this a
            // rescuer could flip a rival team to Dispatched, block it from real work and push a
            // false mission to its members.
            if (!context.User.IsInRole(Roles.Government))
            {
                var own = await FindMemberTeamAsync(db, actorId, ct);
                return own is not null && own.Id == teamId ? own : null;
            }

            return await db.Teams.FirstOrDefaultAsync(x => x.Id == teamId, ct);
        }

        var ownTeam = await FindMemberTeamAsync(db, actorId, ct);
        if (ownTeam is not null)
        {
            return ownTeam;
        }

        if (!context.User.IsInRole(Roles.Rescuer))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var name = context.User.FindFirstValue(ClaimTypes.Name) ?? "Responder";
        var team = new RescueTeam
        {
            Id = Guid.NewGuid(),
            TeamName = $"Unit {name}",
            Specialization = "General",
            TeamLeadUserId = actorId,
            Status = TeamStatus.Available,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        team.Members.Add(new RescueTeamMember { TeamId = team.Id, RescuerUserId = actorId, JoinedAtUtc = now });
        db.Teams.Add(team);
        return team;
    }

    private static Task<RescueTeam?> FindMemberTeamAsync(RescueDbContext db, Guid userId, CancellationToken ct)
        => db.Teams
            .Where(t => t.TeamLeadUserId == userId || t.Members.Any(m => m.RescuerUserId == userId))
            .FirstOrDefaultAsync(ct);

    private static GeoPoint? ResolveOrigin(double? lat, double? lng, RescueTeam? team)
    {
        if (lat is { } latitude && lng is { } longitude)
        {
            return new GeoPoint(latitude, longitude);
        }

        return team is { CurrentLatitude: { } tLat, CurrentLongitude: { } tLng } ? new GeoPoint(tLat, tLng) : null;
    }

    private static async Task NotifyTeamAsync(
        RescueDbContext db,
        IRealtimeNotifier notifier,
        RescueTeam team,
        RescueMission mission,
        IncidentSummaryDto incident,
        CancellationToken ct)
    {
        var memberIds = await db.Teams.AsNoTracking()
            .Where(t => t.Id == team.Id)
            .SelectMany(t => t.Members.Select(m => m.RescuerUserId))
            .ToListAsync(ct);

        if (team.TeamLeadUserId != Guid.Empty)
        {
            memberIds.Add(team.TeamLeadUserId);
        }

        var payload = new
        {
            title = $"New mission: {mission.MissionTitle}",
            missionId = mission.Id,
            incidentId = mission.IncidentId,
            priority = mission.Priority,
            isSos = incident.IsSos,
            latitude = incident.Location.Latitude,
            longitude = incident.Location.Longitude,
        };

        foreach (var memberId in memberIds.Distinct().Where(x => x != Guid.Empty))
        {
            await notifier.NotifyUserAsync(memberId, MissionTopic, payload, ct);
        }
    }

    private static Task NotifyOperationsAsync(
        IRealtimeNotifier notifier,
        string title,
        RescueMission mission,
        CancellationToken ct,
        string topic = OperationsTopic)
        => notifier.NotifyRoleAsync(Roles.Government, topic, new
        {
            title,
            missionId = mission.Id,
            incidentId = mission.IncidentId,
            status = mission.Status.ToString(),
        }, ct);

    private static bool TryGetUserId(HttpContext context, out Guid userId)
        => Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static RescueTeamDto ToTeamDto(RescueTeam team, int activeCount) => new(
        team.Id,
        team.TeamName,
        team.Specialization,
        team.ContactNumber,
        team.Status,
        team.TeamLeadUserId,
        team.CurrentLatitude is { } lat && team.CurrentLongitude is { } lng ? new GeoPoint(lat, lng) : null,
        activeCount);

    private static RescueMissionDto ToDto(RescueMission mission, string teamName) => new(
        mission.Id,
        mission.IncidentId,
        mission.AssignedTeamId,
        teamName,
        mission.MissionTitle,
        mission.Priority,
        mission.Status,
        mission.AssignedAtUtc,
        mission.AcceptedAtUtc,
        mission.StartedAtUtc,
        mission.OnSceneAtUtc,
        mission.CompletedAtUtc,
        mission.OutcomeNotes,
        mission.RejectionReason,
        mission.Logs.OrderBy(l => l.TimestampUtc)
            .Select(l => new MissionLogDto(l.StatusUpdate, l.Message, l.TimestampUtc)).ToList());

    private static IResult Conflict(string title, string detail) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): rescue data is temporarily unavailable.");
}
