using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Features.Realtime.Handlers;

/// <summary>
/// Lock / role change / token reuse must drop live hub connections immediately — the JWT
/// itself stays valid for up to its TTL (D-020), so the socket is the only thing we can kill.
/// Logs the user id and the aborted count only.
/// </summary>
public sealed class AuthEventDisconnectHandler : IEventHandler<AuthEvent>
{
    private static readonly HashSet<string> DisconnectActions =
        new(StringComparer.Ordinal) { "Lock", "RoleChange", "TokenReuse" };

    private readonly HubConnectionRegistry _registry;
    private readonly ILogger<AuthEventDisconnectHandler> _logger;

    public AuthEventDisconnectHandler(HubConnectionRegistry registry, ILogger<AuthEventDisconnectHandler> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public Task HandleAsync(AuthEvent evt, CancellationToken ct = default)
    {
        if (!DisconnectActions.Contains(evt.Action))
        {
            return Task.CompletedTask;
        }

        var aborted = _registry.AbortUser(evt.UserId);
        if (aborted > 0)
        {
            _logger.LogInformation("Auth action {Action} aborted {Count} live hub connections for user {UserId}",
                evt.Action, aborted, evt.UserId);
        }

        return Task.CompletedTask;
    }
}
