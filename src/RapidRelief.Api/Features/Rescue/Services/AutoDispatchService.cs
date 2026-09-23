using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Rescue.Services;

/// <summary>
/// Autonomous dispatch engine for high-priority/catastrophic incidents.
/// Automatically evaluates team suitability, reserves the unit, creates the mission,
/// triggers real-time alerts across channels, and generates audit records.
/// </summary>
public sealed class AutoDispatchService : IAutoDispatchService
{
    public static readonly Guid AutoDispatchActorId = new("00000000-a1a1-a1a1-a1a1-000000000001");

    private readonly RescueDbContext _db;
    private readonly IIncidentReadService _incidents;
    private readonly IEventBus _eventBus;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAuditTrail _audit;
    private readonly DatabaseHealth _health;
    private readonly AutoDispatchOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutoDispatchService> _logger;

    public AutoDispatchService(
        RescueDbContext db,
        IIncidentReadService incidents,
        IEventBus eventBus,
        IRealtimeNotifier notifier,
        IAuditTrail audit,
        DatabaseHealth health,
        AutoDispatchOptions options,
        TimeProvider clock,
        ILogger<AutoDispatchService> logger)
    {
        _db = db;
        _incidents = incidents;
        _eventBus = eventBus;
        _notifier = notifier;
        _audit = audit;
        _health = health;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AutoDispatchOutcome> EvaluateAndDispatchAsync(
        Guid incidentId,
        double priorityScore,
        Severity severity,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new AutoDispatchOutcome(false, null, null, null, null, "Auto-dispatch is disabled in configuration.");
        }

        if (_health.PostgresAvailable == false)
        {
            return new AutoDispatchOutcome(false, null, null, null, null, "Database degraded (D-005): skipping auto-dispatch.");
        }

        var incident = await _incidents.GetByIdAsync(incidentId, ct);
        if (incident is null)
        {
            return new AutoDispatchOutcome(false, null, null, null, null, $"Incident {incidentId} was not found.");
        }

        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Rejected)
        {
            return new AutoDispatchOutcome(false, null, null, null, null, $"Incident {incidentId} is {incident.Status}.");
        }

        // Concurrency / Idempotency guard: never create duplicate active missions for the same incident
        var alreadyHasMission = await _db.Missions.AsNoTracking().AnyAsync(
            m => m.IncidentId == incidentId &&
                 m.Status != MissionStatus.Cancelled &&
                 m.Status != MissionStatus.Completed, ct);

        if (alreadyHasMission)
        {
            return new AutoDispatchOutcome(false, null, null, null, null, $"Incident {incidentId} already has an active rescue mission.");
        }

        // Qualification check: SOS, Catastrophic severity, or numerical score meeting threshold
        var qualifies = (_options.AlwaysDispatchOnSos && incident.IsSos) ||
                        severity == Severity.Catastrophic ||
                        priorityScore >= _options.MinPriorityScore;

        if (!qualifies)
        {
            return new AutoDispatchOutcome(
                false, null, null, null, null,
                $"Priority score {priorityScore:F1} (Severity {severity}) does not meet the auto-dispatch threshold {_options.MinPriorityScore:F1}.");
        }

        var teams = await _db.Teams.Include(t => t.Members).ToListAsync(ct);
        if (teams.Count == 0)
        {
            await RecordNoTeamAuditAsync(incidentId, incident, priorityScore, "No rescue teams registered in the system.", ct);
            return new AutoDispatchOutcome(false, null, null, null, null, "No rescue teams are registered.");
        }

        var activeCounts = await _db.Missions.AsNoTracking()
            .Where(m => m.Status != MissionStatus.Completed && m.Status != MissionStatus.Cancelled)
            .GroupBy(m => m.AssignedTeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, ct);

        // Rank available units using composite suitability (specialization, availability, proximity, load)
        var ranked = TeamSuitabilityScorer.Rank(incident.Location, incident.Type, teams, activeCounts);

        var candidate = ranked.FirstOrDefault(x =>
            !string.Equals(x.Team.Status, TeamStatus.OffDuty, StringComparison.OrdinalIgnoreCase) &&
            x.ActiveMissions == 0 &&
            (x.DistanceKm is null || x.DistanceKm <= _options.MaxUsefulDistanceKm) &&
            x.Score >= _options.MinSuitabilityScore);

        if (candidate is null)
        {
            var detail = $"No available unit met suitability criteria within {_options.MaxUsefulDistanceKm:F0} km (min score {_options.MinSuitabilityScore:P0}).";
            _logger.LogWarning("AI Auto-Dispatch could not find an available team for incident {IncidentId} (Priority: {Priority:F0}, Type: {Type}). Flagging for human coordinator.",
                incidentId, priorityScore, incident.Type);

            await RecordNoTeamAuditAsync(incidentId, incident, priorityScore, detail, ct);
            return new AutoDispatchOutcome(false, null, null, null, null, detail);
        }

        // Concurrency guard: double-check team is not running an active mission that started concurrently
        var teamBusy = await _db.Missions.AsNoTracking().AnyAsync(
            m => m.AssignedTeamId == candidate.Team.Id &&
                 m.Status != MissionStatus.Cancelled &&
                 m.Status != MissionStatus.Completed, ct);

        if (teamBusy)
        {
            _logger.LogInformation("Team {TeamName} was concurrently assigned. Deferring incident {IncidentId} to queue.",
                candidate.Team.TeamName, incidentId);
            return new AutoDispatchOutcome(false, null, null, null, null, $"Team {candidate.Team.TeamName} was committed in a concurrent race.");
        }

        var now = _clock.GetUtcNow();
        var mission = new RescueMission
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            AssignedTeamId = candidate.Team.Id,
            MissionTitle = $"AI Auto-Dispatch: {incident.Type} Response",
            Priority = incident.IsSos || priorityScore >= 75 ? "Critical" : "Urgent",
            Status = MissionStatus.Assigned,
            AssignedByUserId = AutoDispatchActorId,
            AssignedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        mission.Logs.Add(new RescueMissionLog
        {
            MissionId = mission.Id,
            LoggedByUserId = AutoDispatchActorId,
            StatusUpdate = MissionStatus.Assigned.ToString(),
            Message = $"AI Auto-Dispatched to {candidate.Team.TeamName} (Match: {candidate.Score:P0}, Priority: {priorityScore:F0}, Distance: {(candidate.DistanceKm.HasValue ? $"{candidate.DistanceKm.Value:F1} km" : "unknown")}).",
            TimestampUtc = now,
        });

        _db.Missions.Add(mission);
        candidate.Team.Status = TeamStatus.Dispatched;
        candidate.Team.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("AI Auto-Dispatch successful: Incident {IncidentId} assigned to team {TeamName} (Mission: {MissionId}, Suitability: {Score:F2})",
            incidentId, candidate.Team.TeamName, mission.Id, candidate.Score);

        // 1. Publish Domain Event (triggers timeline updates, citizen push notifications)
        await _eventBus.PublishAsync(new MissionAssigned(mission.Id, mission.IncidentId, candidate.Team.Id, AutoDispatchActorId), ct);

        // 2. Real-time push alerts to the team's devices
        await NotifyTeamAsync(candidate.Team, mission, incident, ct);

        // 3. Operations channel notification for government EOC dashboards
        await NotifyOperationsAsync(candidate.Team.TeamName, mission, incident, priorityScore, ct);

        // 4. Audit Trail
        await _audit.RecordAsync(new AuditRecord(
            AutoDispatchActorId, "AI Auto-Dispatch Engine", Roles.Government,
            "AutoDispatch.MissionAssigned", "Incident", incidentId.ToString(),
            $"AI automatically dispatched {candidate.Team.TeamName} to incident {incidentId} ({incident.Type}, Priority: {priorityScore:F0}, Match: {candidate.Score:P0}).",
            "Success"), ct);

        return new AutoDispatchOutcome(
            true, mission.Id, candidate.Team.Id, candidate.Team.TeamName, candidate.Score,
            $"Automatically dispatched {candidate.Team.TeamName} with suitability score {candidate.Score:P0}.");
    }

    private async Task RecordNoTeamAuditAsync(Guid incidentId, IncidentSummaryDto incident, double priorityScore, string reason, CancellationToken ct)
    {
        try
        {
            await _audit.RecordAsync(new AuditRecord(
                AutoDispatchActorId, "AI Auto-Dispatch Engine", Roles.Government,
                "AutoDispatch.Unassigned", "Incident", incidentId.ToString(),
                $"Incident {incidentId} (Priority: {priorityScore:F0}, {incident.Type}, SOS: {incident.IsSos}) qualified for auto-dispatch but could not be dispatched: {reason}",
                "PendingManualDispatch"), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record unassigned audit for incident {IncidentId}", incidentId);
        }
    }

    private async Task NotifyTeamAsync(RescueTeam team, RescueMission mission, IncidentSummaryDto incident, CancellationToken ct)
    {
        var memberIds = team.Members.Select(m => m.RescuerUserId).ToList();
        if (team.TeamLeadUserId != Guid.Empty)
        {
            memberIds.Add(team.TeamLeadUserId);
        }

        var payload = new
        {
            title = $"URGENT: AI Auto-Dispatched to {incident.Type}",
            missionId = mission.Id,
            incidentId = mission.IncidentId,
            priority = mission.Priority,
            isSos = incident.IsSos,
            latitude = incident.Location.Latitude,
            longitude = incident.Location.Longitude,
        };

        foreach (var memberId in memberIds.Distinct().Where(x => x != Guid.Empty))
        {
            await _notifier.NotifyUserAsync(memberId, RealtimeTopics.RescueMissionAssigned, payload, ct);
        }
    }

    private async Task NotifyOperationsAsync(string teamName, RescueMission mission, IncidentSummaryDto incident, double priorityScore, CancellationToken ct)
    {
        var payload = new
        {
            title = $"AI Auto-Dispatched: {teamName} → {incident.Type} (Priority {priorityScore:F0})",
            missionId = mission.Id,
            incidentId = mission.IncidentId,
            status = mission.Status.ToString(),
            autoDispatched = true,
        };

        await _notifier.NotifyRoleAsync(Roles.Government, RealtimeTopics.RescueOperations, payload, ct);
        await _notifier.NotifyRoleAsync(Roles.Rescuer, RealtimeTopics.RescueOperations, payload, ct);
    }
}
