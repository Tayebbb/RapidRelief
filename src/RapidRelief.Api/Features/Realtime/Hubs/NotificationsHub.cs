using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RapidRelief.Api.Features.Realtime.Hubs;

/// <summary>
/// Push-only notification hub: authenticated, ZERO invokable methods. Group membership is
/// derived server-side from the caller's role claims — never from a client argument.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string MethodName = "notification";

    private readonly HubConnectionRegistry _registry;
    private readonly ILogger<NotificationsHub> _logger;

    public NotificationsHub(HubConnectionRegistry registry, ILogger<NotificationsHub> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public static string RoleGroup(string role) => $"role:{role}";

    public override async Task OnConnectedAsync()
    {
        // Registration first: a connection the D-040 registry refuses cannot be kicked later
        // (Lock/RoleChange/TokenReuse all go through the registry), so it must not exist at
        // all — and it must never reach a role group on the way to being refused.
        if (!TryGetUserId(out var userId))
        {
            _logger.LogWarning("Hub connection {ConnectionId} has no usable user id — refused",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        if (!_registry.Add(userId, Context))
        {
            _logger.LogWarning("Hub connection {ConnectionId} for user {UserId} exceeded a registry cap — refused",
                Context.ConnectionId, userId);
            Context.Abort();
            return;
        }

        foreach (var role in Context.User?.FindAll(ClaimTypes.Role) ?? [])
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoleGroup(role.Value));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId))
        {
            _registry.Remove(userId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private bool TryGetUserId(out Guid userId)
        => Guid.TryParse(Context.UserIdentifier, out userId);
}
