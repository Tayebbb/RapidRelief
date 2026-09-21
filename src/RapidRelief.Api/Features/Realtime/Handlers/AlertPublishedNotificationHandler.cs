using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Realtime.Handlers;

/// <summary>Feature-local push payload — Title is duck-typed into Summary by D-037.</summary>
public sealed record AlertPublishedNotification(
    Guid AlertId,
    string Title,
    string Body,
    Severity Severity,
    DisasterType? Type,
    DateTimeOffset ExpiresAtUtc);

/// <summary>D-036: AlertPublished → topic <c>alerts.published</c> to everyone (dormant until F10 publishes).</summary>
public sealed class AlertPublishedNotificationHandler : IEventHandler<AlertPublished>
{
    public const string Topic = RealtimeTopics.AlertPublished;

    private readonly IRealtimeNotifier _notifier;

    public AlertPublishedNotificationHandler(IRealtimeNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(AlertPublished evt, CancellationToken ct = default)
        => _notifier.NotifyAllAsync(Topic, new AlertPublishedNotification(evt.AlertId, evt.Title, evt.Body,
            evt.Severity, evt.Type, evt.ExpiresAtUtc), ct);
}
