using System.Reflection;
using RapidRelief.Api.Features.Realtime.Hubs;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// The hub is push-only by design: any invokable method would be a new client-callable
/// surface on an authenticated socket. This pins that nobody adds one.
/// </summary>
public sealed class HubSurfaceTests
{
    [Fact]
    public void The_hub_exposes_no_client_invokable_methods()
    {
        var invokable = typeof(NotificationsHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetBaseDefinition().DeclaringType == typeof(NotificationsHub))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(invokable);
    }
}
