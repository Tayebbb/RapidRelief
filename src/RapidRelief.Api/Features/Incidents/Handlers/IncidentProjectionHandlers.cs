using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Incidents.Endpoints;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Incidents.Handlers;

/// <summary>Pushes a new report straight to the responder role groups so queues update live.</summary>
public sealed class IncidentCreatedNotificationHandler(IRealtimeNotifier notifier) : IEventHandler<IncidentCreated>
{
    public async Task HandleAsync(IncidentCreated evt, CancellationToken ct = default)
    {
        var payload = new
        {
            title = evt.IsSos ? "SOS report received" : $"New {evt.Type} report",
            incidentId = evt.IncidentId,
            type = evt.Type.ToString(),
            severity = evt.ReportedSeverity.ToString(),
            isSos = evt.IsSos,
            latitude = evt.Location.Latitude,
            longitude = evt.Location.Longitude,
        };

        await notifier.NotifyRoleAsync(Roles.Rescuer, Topics.IncidentReported, payload, ct);
        await notifier.NotifyRoleAsync(Roles.Government, Topics.IncidentReported, payload, ct);
    }
}

/// <summary>
/// F8 owns the assessment; the incident row keeps a projection of it so the rescue queue can sort
/// by priority without reading another slice's tables (§4.2 — events are the only write path).
/// No citizen notification here: triage is not something they can act on, and the submit screen
/// already confirmed receipt (notifications stay reserved for actionable steps).
/// </summary>
public sealed class IncidentAssessedProjectionHandler(
    IncidentsDbContext db,
    DatabaseHealth health,
    ILogger<IncidentAssessedProjectionHandler> logger) : IEventHandler<IncidentAssessed>
{
    public async Task HandleAsync(IncidentAssessed evt, CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return;
        }

        var incident = await db.Reports.FirstOrDefaultAsync(x => x.Id == evt.IncidentId, ct);
        if (incident is null)
        {
            return;
        }

        incident.PriorityScore = evt.PriorityScore;
        incident.AiSummary = evt.Summary;
        incident.AiSeverityScore = (int)evt.EstimatedSeverity;
        incident.PossibleDuplicateOfId = evt.PossibleDuplicateOfId;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Incident {IncidentId} assessed: priority {Priority}", evt.IncidentId, evt.PriorityScore);
    }
}

/// <summary>Rescue owns missions; the incident lifecycle follows the mission through events only.</summary>
public sealed class MissionAssignedProjectionHandler(
    IncidentsDbContext db,
    IRealtimeNotifier notifier,
    DatabaseHealth health,
    TimeProvider clock) : IEventHandler<MissionAssigned>
{
    public async Task HandleAsync(MissionAssigned evt, CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return;
        }

        var incident = await db.Reports.Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == evt.IncidentId, ct);
        if (incident is null || incident.Status is IncidentStatus.Resolved or IncidentStatus.Rejected)
        {
            return;
        }

        incident.AssignedMissionId = evt.MissionId;
        incident.AssignedTeamId = evt.TeamId;
        incident.AssignedAtUtc = clock.GetUtcNow();
        incident.MissionStage = MissionStatus.Assigned.ToString();
        IncidentsEndpoints.AppendStatus(db, incident, IncidentStatus.Assigned, evt.AssignedByUserId,
            "Rescue team assigned", clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        await IncidentsEndpoints.NotifyReporterAsync(notifier, incident, "A rescue team has been assigned to your report.", ct);
    }
}

/// <summary>Mission progress drives the citizen-visible incident status and their notification.</summary>
public sealed class MissionStatusProjectionHandler(
    IncidentsDbContext db,
    IRealtimeNotifier notifier,
    DatabaseHealth health,
    TimeProvider clock) : IEventHandler<MissionStatusChanged>
{
    public async Task HandleAsync(MissionStatusChanged evt, CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return;
        }

        var incident = await db.Reports.Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == evt.IncidentId, ct);
        if (incident is null || incident.Status == IncidentStatus.Rejected)
        {
            return;
        }

        var (status, message) = evt.NewStatus switch
        {
            MissionStatus.EnRoute => (IncidentStatus.InProgress, "Your rescue team is on the way."),
            MissionStatus.OnScene => (IncidentStatus.InProgress, "Your rescue team has arrived on scene."),
            MissionStatus.Completed => (IncidentStatus.Resolved, "Your report has been resolved. Stay safe."),
            MissionStatus.Cancelled => (IncidentStatus.Verified, "The assigned mission was cancelled — your report is back in the queue."),
            _ => (IncidentStatus.Assigned, "A rescue team has been assigned to your report."),
        };

        var now = clock.GetUtcNow();
        if (evt.NewStatus == MissionStatus.Cancelled)
        {
            incident.AssignedMissionId = null;
            incident.AssignedTeamId = null;
            incident.AssignedAtUtc = null;
            incident.MissionStage = null;
        }
        else
        {
            incident.MissionStage = evt.NewStatus.ToString();
        }

        if (incident.Status != status)
        {
            IncidentsEndpoints.AppendStatus(db, incident, status, Guid.Empty, $"Mission {evt.NewStatus}", now);
        }
        else
        {
            // EnRoute and OnScene share IncidentStatus.InProgress, but the citizen must still see
            // both steps with their own timestamps — record the stage as its own timeline entry.
            db.StatusHistory.Add(new Domain.IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = incident.Status,
                ToStatus = status,
                ChangedByUserId = Guid.Empty,
                Notes = $"Mission {evt.NewStatus}",
                ChangedAtUtc = now,
            });
            incident.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
        await IncidentsEndpoints.NotifyReporterAsync(notifier, incident, message, ct);
    }
}
