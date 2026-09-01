using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Realtime.Handlers;

/// <summary>Feature-local push payload — Summary is duck-typed out of it by D-037.</summary>
public sealed record IncidentAssessedNotification(
    Guid IncidentId,
    Severity EstimatedSeverity,
    double PriorityScore,
    string Summary,
    Guid? PossibleDuplicateOfId);

/// <summary>D-036: IncidentAssessed → topic <c>ai.incident.assessed</c> for Rescue + Admin.</summary>
public sealed class IncidentAssessedNotificationHandler : IEventHandler<IncidentAssessed>
{
    public const string Topic = "ai.incident.assessed";

    private readonly IRealtimeNotifier _notifier;

    public IncidentAssessedNotificationHandler(IRealtimeNotifier notifier) => _notifier = notifier;

    public async Task HandleAsync(IncidentAssessed evt, CancellationToken ct = default)
    {
        var payload = new IncidentAssessedNotification(evt.IncidentId, evt.EstimatedSeverity,
            evt.PriorityScore, evt.Summary, evt.PossibleDuplicateOfId);

        await _notifier.NotifyRoleAsync(Roles.Rescue, Topic, payload, ct);
        await _notifier.NotifyRoleAsync(Roles.Admin, Topic, payload, ct);
    }
}
