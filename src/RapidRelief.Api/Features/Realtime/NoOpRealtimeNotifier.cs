using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Realtime;

/// <summary>Permanent no-op fallback (blueprint B4): Debug log + CompletedTask. F9 adds SignalR in this lane.</summary>
public sealed class NoOpRealtimeNotifier : IRealtimeNotifier
{
    private readonly ILogger<NoOpRealtimeNotifier> _logger;

    public NoOpRealtimeNotifier(ILogger<NoOpRealtimeNotifier> logger) => _logger = logger;

    public Task NotifyAllAsync(string topic, object payload, CancellationToken ct = default)
    {
        _logger.LogDebug("No-op realtime notify (all): {Topic}", topic);
        return Task.CompletedTask;
    }

    public Task NotifyRoleAsync(string role, string topic, object payload, CancellationToken ct = default)
    {
        _logger.LogDebug("No-op realtime notify (role {Role}): {Topic}", role, topic);
        return Task.CompletedTask;
    }

    public Task NotifyUserAsync(Guid userId, string topic, object payload, CancellationToken ct = default)
    {
        _logger.LogDebug("No-op realtime notify (user {UserId}): {Topic}", userId, topic);
        return Task.CompletedTask;
    }
}
