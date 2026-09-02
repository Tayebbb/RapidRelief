using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

// Folder/namespace is "Hubs" (blueprint says "Hub"): a namespace segment named Hub would
// shadow SignalR's Hub base type inside this feature.
namespace RapidRelief.Api.Features.Realtime.Hubs;

/// <summary>
/// D-040 bounded live-connection registry: the only place F9 holds framework object
/// references. Over-cap connections are simply not tracked (they still die at token expiry);
/// empty per-user buckets are pruned on disconnect.
/// </summary>
public sealed class HubConnectionRegistry
{
    public const int MaxConnectionsPerUser = 10;
    public const int MaxTrackedUsers = 2000;

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, HubCallerContext>> _connections = new();
    private readonly ILogger<HubConnectionRegistry> _logger;

    public HubConnectionRegistry(ILogger<HubConnectionRegistry> logger) => _logger = logger;

    public int TrackedUserCount => _connections.Count;

    public int ConnectionCount(Guid userId)
        => _connections.TryGetValue(userId, out var bucket) ? bucket.Count : 0;

    /// <summary>False = a D-040 cap refused tracking; the caller must not treat that as an error.</summary>
    public bool Add(Guid userId, HubCallerContext context)
    {
        if (!_connections.TryGetValue(userId, out var bucket))
        {
            if (_connections.Count >= MaxTrackedUsers)
            {
                _logger.LogWarning("Hub connection registry is at its {Cap}-user cap — connection not tracked",
                    MaxTrackedUsers);
                return false;
            }
            bucket = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, HubCallerContext>());
        }

        if (bucket.Count >= MaxConnectionsPerUser)
        {
            _logger.LogWarning("User {UserId} is at the {Cap}-connection cap — connection not tracked",
                userId, MaxConnectionsPerUser);
            return false;
        }

        bucket[context.ConnectionId] = context;
        return true;
    }

    public void Remove(Guid userId, string connectionId)
    {
        if (!_connections.TryGetValue(userId, out var bucket))
        {
            return;
        }

        bucket.TryRemove(connectionId, out _);
        if (bucket.IsEmpty)
        {
            // Key-AND-value removal: a connection added between IsEmpty and this call keeps its bucket.
            _connections.TryRemove(
                new KeyValuePair<Guid, ConcurrentDictionary<string, HubCallerContext>>(userId, bucket));
        }
    }

    /// <summary>Aborts every tracked connection for the user; returns how many were aborted.</summary>
    public int AbortUser(Guid userId)
    {
        if (!_connections.TryGetValue(userId, out var bucket))
        {
            return 0;
        }

        var aborted = 0;
        foreach (var context in bucket.Values)
        {
            try
            {
                context.Abort();
                aborted++;
            }
            catch (Exception ex)
            {
                // An already-torn-down connection must never block the remaining kicks.
                _logger.LogWarning(ex, "Aborting a hub connection for user {UserId} failed", userId);
            }
        }

        return aborted;
    }
}
