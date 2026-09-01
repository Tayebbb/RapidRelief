using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Features.Realtime.Endpoints;
using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Realtime;

/// <summary>
/// D-032 Hub/PollingOnly notifier: persists a notification row, then pushes it to the
/// matching SignalR audience. Every failure is swallowed and logged — the event bus runs
/// handlers inline in the publisher's request (D-006), so a hub or DB fault must never
/// surface in F2/F8/F10.
/// </summary>
public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotificationsHub>? _hubContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(
        IServiceScopeFactory scopeFactory,
        IHubContext<NotificationsHub>? hubContext,
        TimeProvider timeProvider,
        ILogger<SignalRRealtimeNotifier> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task NotifyAllAsync(string topic, object payload, CancellationToken ct = default)
        => PublishAsync(NotificationAudience.All, role: null, userId: null, topic, payload, ct);

    public Task NotifyRoleAsync(string role, string topic, object payload, CancellationToken ct = default)
        => PublishAsync(NotificationAudience.Role, role, userId: null, topic, payload, ct);

    public Task NotifyUserAsync(Guid userId, string topic, object payload, CancellationToken ct = default)
        => PublishAsync(NotificationAudience.User, role: null, userId, topic, payload, ct);

    private async Task PublishAsync(
        string audience, string? role, Guid? userId, string topic, object payload, CancellationToken ct)
    {
        var sanitizedTopic = SanitizeTopic(topic);
        try
        {
            var json = JsonSerializer.Serialize(payload, SerializerOptions);
            if (json.Length > Notification.MaxPayloadChars)
            {
                // D-033: metadata only — the payload itself must never reach the logs.
                _logger.LogError(
                    "Notification dropped: payload is {Length} chars (cap {Cap}) for topic {Topic}, audience {Audience}",
                    json.Length, Notification.MaxPayloadChars, sanitizedTopic, audience);
                return;
            }

            if (!string.Equals(sanitizedTopic, topic, StringComparison.Ordinal))
            {
                _logger.LogWarning("Notification topic {Topic} violated the convention and was rewritten to {Sanitized}",
                    topic, sanitizedTopic);
            }

            var notification = new NotificationDto(
                Guid.NewGuid(), sanitizedTopic, DeriveSummary(json, sanitizedTopic), json,
                audience, role, userId, _timeProvider.GetUtcNow(), IsRead: false);

            try
            {
                await PersistAsync(notification, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // History is the degradable half: a live push still beats losing the alert.
                _logger.LogError(ex,
                    "Notification {Topic} could not be persisted — pushing live only, audience {Audience}",
                    sanitizedTopic, audience);
            }

            await PushAsync(notification, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Load-bearing (blueprint risk 3): publishers run this inline and must never break.
            _logger.LogError(ex, "Realtime notification failed for topic {Topic}, audience {Audience}",
                sanitizedTopic, audience);
        }
    }

    private async Task PersistAsync(NotificationDto notification, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var health = scope.ServiceProvider.GetRequiredService<DatabaseHealth>();
        if (health.PostgresAvailable != true)
        {
            // D-028 analogue: degraded means live pushes still work, history does not.
            _logger.LogWarning("Database degraded — notification {Topic} pushed but not persisted", notification.Topic);
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Notifications.Add(new Notification
        {
            Id = notification.Id,
            Audience = notification.Audience,
            Role = notification.Role,
            UserId = notification.UserId,
            Topic = notification.Topic,
            Summary = notification.Summary,
            PayloadJson = notification.PayloadJson,
            CreatedAtUtc = notification.CreatedAtUtc,
        });
        await db.SaveChangesAsync(ct);
    }

    private Task PushAsync(NotificationDto notification, CancellationToken ct)
    {
        if (_hubContext is null)
        {
            return Task.CompletedTask; // PollingOnly: the endpoints serve the inbox instead.
        }

        return notification.Audience switch
        {
            NotificationAudience.Role when notification.Role is not null =>
                _hubContext.Clients.Group(NotificationsHub.RoleGroup(notification.Role))
                    .SendAsync(NotificationsHub.MethodName, notification, ct),
            NotificationAudience.User when notification.UserId is not null =>
                _hubContext.Clients.User(notification.UserId.Value.ToString("D"))
                    .SendAsync(NotificationsHub.MethodName, notification, ct),
            _ => _hubContext.Clients.All.SendAsync(NotificationsHub.MethodName, notification, ct),
        };
    }

    /// <summary>D-036: lowercase <c>[a-z0-9.]</c>, ≤64 chars.</summary>
    private static string SanitizeTopic(string topic)
    {
        var builder = new StringBuilder(topic.Length);
        foreach (var c in topic.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.')
            {
                builder.Append(c);
            }
            if (builder.Length == Notification.MaxTopicChars)
            {
                break;
            }
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    /// <summary>D-037: duck-type a renderable line out of the serialized payload, else the topic.</summary>
    private static string DeriveSummary(string json, string topic)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return topic;
            }

            var candidate = FindString(document.RootElement, "title") ?? FindString(document.RootElement, "summary");
            return string.IsNullOrWhiteSpace(candidate) ? topic : Clean(candidate);
        }
        catch (JsonException)
        {
            return topic;
        }
    }

    private static string? FindString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length <= Notification.MaxSummaryChars
            ? cleaned
            : cleaned[..Notification.MaxSummaryChars];
    }
}
