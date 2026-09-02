using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Realtime.Hubs;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>D-040 — the bespoke kick registry must stay bounded and leak nothing on disconnect.</summary>
public sealed class HubConnectionRegistryTests
{
    private static HubConnectionRegistry CreateRegistry()
        => new(NullLogger<HubConnectionRegistry>.Instance);

    [Fact]
    public void Added_connections_are_tracked_per_user()
    {
        var registry = CreateRegistry();
        var user = Guid.NewGuid();

        Assert.True(registry.Add(user, new FakeHubCallerContext("c1")));
        Assert.True(registry.Add(user, new FakeHubCallerContext("c2")));

        Assert.Equal(2, registry.ConnectionCount(user));
        Assert.Equal(1, registry.TrackedUserCount);
    }

    [Fact]
    public void Removing_the_last_connection_prunes_the_user_bucket()
    {
        var registry = CreateRegistry();
        var user = Guid.NewGuid();
        registry.Add(user, new FakeHubCallerContext("c1"));
        registry.Add(user, new FakeHubCallerContext("c2"));

        registry.Remove(user, "c1");
        registry.Remove(user, "c2");

        Assert.Equal(0, registry.ConnectionCount(user));
        Assert.Equal(0, registry.TrackedUserCount);
    }

    [Fact]
    public void Removing_an_unknown_connection_is_a_no_op()
    {
        var registry = CreateRegistry();

        registry.Remove(Guid.NewGuid(), "ghost");

        Assert.Equal(0, registry.TrackedUserCount);
    }

    [Fact]
    public void Connections_beyond_the_per_user_cap_are_not_tracked()
    {
        var registry = CreateRegistry();
        var user = Guid.NewGuid();
        for (var i = 0; i < HubConnectionRegistry.MaxConnectionsPerUser; i++)
        {
            Assert.True(registry.Add(user, new FakeHubCallerContext($"c{i}")));
        }

        var overCap = new FakeHubCallerContext("over-cap");
        var tracked = registry.Add(user, overCap);

        Assert.False(tracked);
        Assert.Equal(HubConnectionRegistry.MaxConnectionsPerUser, registry.ConnectionCount(user));
        Assert.Equal(HubConnectionRegistry.MaxConnectionsPerUser, registry.AbortUser(user));
        Assert.Equal(0, overCap.AbortCount);
    }

    [Fact]
    public void Abort_user_aborts_every_tracked_connection_and_reports_the_count()
    {
        var registry = CreateRegistry();
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        var first = new FakeHubCallerContext("c1");
        var second = new FakeHubCallerContext("c2");
        var untouched = new FakeHubCallerContext("c3");
        registry.Add(user, first);
        registry.Add(user, second);
        registry.Add(other, untouched);

        var aborted = registry.AbortUser(user);

        Assert.Equal(2, aborted);
        Assert.Equal(1, first.AbortCount);
        Assert.Equal(1, second.AbortCount);
        Assert.Equal(0, untouched.AbortCount);
    }

    [Fact]
    public void Abort_user_for_an_unknown_user_returns_zero()
    {
        var registry = CreateRegistry();

        Assert.Equal(0, registry.AbortUser(Guid.NewGuid()));
    }

    [Fact]
    public void A_throwing_connection_does_not_stop_the_remaining_aborts()
    {
        var registry = CreateRegistry();
        var user = Guid.NewGuid();
        registry.Add(user, new ThrowingHubCallerContext("bad"));
        var good = new FakeHubCallerContext("good");
        registry.Add(user, good);

        var aborted = registry.AbortUser(user);

        Assert.Equal(1, aborted);
        Assert.Equal(1, good.AbortCount);
    }

    [Fact]
    public void Tracked_users_are_capped()
    {
        var registry = CreateRegistry();
        for (var i = 0; i < HubConnectionRegistry.MaxTrackedUsers; i++)
        {
            registry.Add(Guid.NewGuid(), new FakeHubCallerContext($"c{i}"));
        }

        var overflowUser = Guid.NewGuid();
        var tracked = registry.Add(overflowUser, new FakeHubCallerContext("overflow"));

        Assert.False(tracked);
        Assert.Equal(HubConnectionRegistry.MaxTrackedUsers, registry.TrackedUserCount);
        Assert.Equal(0, registry.ConnectionCount(overflowUser));
    }
}

internal class FakeHubCallerContext : HubCallerContext
{
    private readonly CancellationTokenSource _aborted = new();

    public FakeHubCallerContext(string connectionId) => ConnectionId = connectionId;

    public int AbortCount { get; private set; }

    public override string ConnectionId { get; }

    public override string? UserIdentifier => null;

    public override ClaimsPrincipal? User => null;

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public override IFeatureCollection Features { get; } = new FeatureCollection();

    public override CancellationToken ConnectionAborted => _aborted.Token;

    public override void Abort()
    {
        AbortCount++;
        _aborted.Cancel();
    }
}

internal sealed class ThrowingHubCallerContext : FakeHubCallerContext
{
    public ThrowingHubCallerContext(string connectionId) : base(connectionId)
    {
    }

    public override void Abort() => throw new ObjectDisposedException(nameof(ThrowingHubCallerContext));
}
