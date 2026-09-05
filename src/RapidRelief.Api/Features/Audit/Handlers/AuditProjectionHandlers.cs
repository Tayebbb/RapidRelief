using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Audit.Handlers;

/// <summary>
/// Projects the bus events that represent a decision someone made onto the trail. Reporting an
/// incident is not an administrative action, so <c>IncidentCreated</c> is deliberately absent.
/// </summary>
public sealed class IncidentVerifiedAuditHandler : IEventHandler<IncidentVerified>
{
    private readonly IAuditTrail _audit;

    public IncidentVerifiedAuditHandler(IAuditTrail audit) => _audit = audit;

    public Task HandleAsync(IncidentVerified evt, CancellationToken ct = default) =>
        _audit.RecordAsync(new AuditRecord(
            evt.VerifiedByUserId, string.Empty, string.Empty,
            evt.Approved ? "Incident.Verify" : "Incident.Reject",
            "Incident", evt.IncidentId.ToString(),
            evt.Approved ? "Incident verified for dispatch" : $"Incident rejected: {evt.Reason ?? "no reason given"}",
            evt.Approved ? "Verified" : "Rejected"), ct);
}

public sealed class MissionAssignedAuditHandler : IEventHandler<MissionAssigned>
{
    private readonly IAuditTrail _audit;

    public MissionAssignedAuditHandler(IAuditTrail audit) => _audit = audit;

    public Task HandleAsync(MissionAssigned evt, CancellationToken ct = default) =>
        _audit.RecordAsync(new AuditRecord(
            evt.AssignedByUserId, string.Empty, string.Empty,
            "Mission.Assign", "Mission", evt.MissionId.ToString(),
            $"Team {evt.TeamId} assigned to incident {evt.IncidentId}", "Assigned"), ct);
}

public sealed class MissionStatusAuditHandler : IEventHandler<MissionStatusChanged>
{
    private readonly IAuditTrail _audit;

    public MissionStatusAuditHandler(IAuditTrail audit) => _audit = audit;

    public Task HandleAsync(MissionStatusChanged evt, CancellationToken ct = default) =>
        _audit.RecordAsync(new AuditRecord(
            null, string.Empty, string.Empty,
            "Mission.Status", "Mission", evt.MissionId.ToString(),
            $"Mission moved to {evt.NewStatus} on incident {evt.IncidentId}", evt.NewStatus.ToString()), ct);
}

public sealed class AlertPublishedAuditHandler : IEventHandler<AlertPublished>
{
    private readonly IAuditTrail _audit;

    public AlertPublishedAuditHandler(IAuditTrail audit) => _audit = audit;

    public Task HandleAsync(AlertPublished evt, CancellationToken ct = default) =>
        _audit.RecordAsync(new AuditRecord(
            null, string.Empty, string.Empty,
            "Alert.Publish", "Alert", evt.AlertId.ToString(),
            $"Broadcast \"{evt.Title}\" at severity {evt.Severity} until {evt.ExpiresAtUtc:u}", "Published"), ct);
}

public sealed class ReliefStatusAuditHandler : IEventHandler<ReliefStatusChanged>
{
    private readonly IAuditTrail _audit;

    public ReliefStatusAuditHandler(IAuditTrail audit) => _audit = audit;

    public Task HandleAsync(ReliefStatusChanged evt, CancellationToken ct = default) =>
        _audit.RecordAsync(new AuditRecord(
            null, string.Empty, string.Empty,
            "Relief.Status", "ReliefRequest", evt.RequestId.ToString(),
            $"Relief request moved to {evt.NewStatus}", evt.NewStatus.ToString()), ct);
}

/// <summary>
/// Security events that have no operator endpoint of their own. Lock, unlock and role changes are
/// recorded by the admin endpoints with richer wording, so recording them here too would duplicate.
/// </summary>
public sealed class AuthEventAuditHandler : IEventHandler<AuthEvent>
{
    private static readonly string[] Recorded = ["TokenReuse", "LoginFailed"];

    private readonly IAuditTrail _audit;

    public AuthEventAuditHandler(IAuditTrail audit) => _audit = audit;

    public Task HandleAsync(AuthEvent evt, CancellationToken ct = default)
    {
        if (!Recorded.Contains(evt.Action, StringComparer.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        return _audit.RecordAsync(new AuditRecord(
            null, string.Empty, string.Empty,
            $"Auth.{evt.Action}", "User", evt.UserId.ToString(),
            evt.Details ?? evt.Action, evt.Action), ct);
    }
}
