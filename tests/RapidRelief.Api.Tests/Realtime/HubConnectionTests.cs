using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// Hub integration over TestServer long polling: role groups are server-derived, user pushes
/// are targeted, and a locked user's socket dies immediately.
/// </summary>
public sealed class HubConnectionTests : IClassFixture<TestingWebAppFactory>, IAsyncLifetime
{
    private readonly TestingWebAppFactory _factory;
    private readonly List<HubConnection> _connections = [];

    public HubConnectionTests(TestingWebAppFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }
    }

    private IRealtimeNotifier Notifier => _factory.Services.GetRequiredService<IRealtimeNotifier>();

    private HubConnectionRegistry Registry => _factory.Services.GetRequiredService<HubConnectionRegistry>();

    private async Task<HubConnection> ConnectAsync(string role, bool automaticReconnect = false)
    {
        var connection = RealtimeTestSupport.BuildConnection(_factory, role, automaticReconnect);
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task An_authenticated_client_connects()
    {
        var connection = await ConnectAsync(Roles.Admin);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task A_role_broadcast_reaches_that_role_only()
    {
        var admin = await ConnectAsync(Roles.Admin);
        var citizen = await ConnectAsync(Roles.Citizen);
        var adminInbox = RealtimeTestSupport.Listen(admin);
        var citizenInbox = RealtimeTestSupport.Listen(citizen);

        await Notifier.NotifyRoleAsync(Roles.Admin, "hub.role.only", new { Title = "admins only" });

        var received = await adminInbox.NextAsync();
        Assert.Equal("hub.role.only", received.Topic);
        Assert.Equal("admins only", received.Summary);
        Assert.Equal("Role", received.Audience);
        await citizenInbox.AssertNothingArrivesAsync();
    }

    [Fact]
    public async Task A_broadcast_to_all_reaches_every_connection()
    {
        var admin = await ConnectAsync(Roles.Admin);
        var citizen = await ConnectAsync(Roles.Citizen);
        var adminInbox = RealtimeTestSupport.Listen(admin);
        var citizenInbox = RealtimeTestSupport.Listen(citizen);

        await Notifier.NotifyAllAsync("hub.everyone", new { Title = "everyone" });

        Assert.Equal("hub.everyone", (await adminInbox.NextAsync()).Topic);
        Assert.Equal("hub.everyone", (await citizenInbox.NextAsync()).Topic);
    }

    [Fact]
    public async Task A_user_targeted_push_reaches_only_that_user()
    {
        var rescue = await ConnectAsync(Roles.Rescue);
        var citizen = await ConnectAsync(Roles.Citizen);
        var rescueInbox = RealtimeTestSupport.Listen(rescue);
        var citizenInbox = RealtimeTestSupport.Listen(citizen);

        await Notifier.NotifyUserAsync(RealtimeTestSupport.RescueId, "hub.user.only", new { Title = "for you" });

        var received = await rescueInbox.NextAsync();
        Assert.Equal("User", received.Audience);
        Assert.Equal(RealtimeTestSupport.RescueId, received.UserId);
        await citizenInbox.AssertNothingArrivesAsync();
    }

    [Fact]
    public async Task Connections_are_tracked_and_released_on_disconnect()
    {
        var connection = await ConnectAsync(Roles.Ngo);

        Assert.Equal(1, Registry.ConnectionCount(RealtimeTestSupport.NgoId));

        await connection.StopAsync();

        await WaitUntilAsync(() => Registry.ConnectionCount(RealtimeTestSupport.NgoId) == 0);
        Assert.Equal(0, Registry.ConnectionCount(RealtimeTestSupport.NgoId));
    }

    [Fact]
    public async Task Locking_a_user_aborts_their_live_connection()
    {
        var connection = await ConnectAsync(Roles.Citizen);
        Assert.Equal(HubConnectionState.Connected, connection.State);

        await RealtimeTestSupport.PublishAsync(_factory.Services,
            new AuthEvent(RealtimeTestSupport.CitizenId, "Lock", null));

        await WaitUntilAsync(() => connection.State == HubConnectionState.Disconnected);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    [Fact]
    public async Task An_unrelated_auth_event_leaves_connections_alone()
    {
        var connection = await ConnectAsync(Roles.Admin);

        await RealtimeTestSupport.PublishAsync(_factory.Services,
            new AuthEvent(RealtimeTestSupport.AdminId, "Login", null));
        await Task.Delay(RealtimeTestSupport.SilenceWindow);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task A_connection_past_the_per_user_cap_is_refused_instead_of_running_untracked()
    {
        for (var i = 0; i < HubConnectionRegistry.MaxConnectionsPerUser; i++)
        {
            await ConnectAsync(Roles.Rescue);
        }
        Assert.Equal(HubConnectionRegistry.MaxConnectionsPerUser,
            Registry.ConnectionCount(RealtimeTestSupport.RescueId));

        var overCap = RealtimeTestSupport.BuildConnection(_factory, Roles.Rescue);
        _connections.Add(overCap);
        var listener = RealtimeTestSupport.Listen(overCap);
        try
        {
            await overCap.StartAsync();
        }
        catch (Exception)
        {
            // The server aborts the connection during the handshake — that IS the refusal.
        }

        await WaitUntilAsync(() => overCap.State == HubConnectionState.Disconnected);
        Assert.Equal(HubConnectionState.Disconnected, overCap.State);
        Assert.Equal(HubConnectionRegistry.MaxConnectionsPerUser,
            Registry.ConnectionCount(RealtimeTestSupport.RescueId));

        // An untracked connection must never be in a role group either.
        await Notifier.NotifyRoleAsync(Roles.Rescue, "hub.over.cap", new { Title = "not for you" });
        await listener.AssertNothingArrivesAsync();
    }

    /// <summary>
    /// D-046 residual, pinned deliberately: the kick lands, but nothing on the (re)connect path
    /// re-checks lock state, so the same identity is accepted straight back until its access
    /// token expires (D-020). F1's refresh path stays the enforcement point.
    /// </summary>
    [Fact]
    public async Task A_locked_user_is_kicked_but_the_hub_does_not_re_check_lock_state_on_reconnect()
    {
        var connection = await ConnectAsync(Roles.Citizen, automaticReconnect: true);
        var left = new TaskCompletionSource();
        connection.Reconnecting += _ =>
        {
            left.TrySetResult();
            return Task.CompletedTask;
        };
        connection.Closed += _ =>
        {
            left.TrySetResult();
            return Task.CompletedTask;
        };

        await RealtimeTestSupport.PublishAsync(_factory.Services,
            new AuthEvent(RealtimeTestSupport.CitizenId, "Lock", null));

        await left.Task.WaitAsync(RealtimeTestSupport.ReceiveTimeout);

        var reconnected = await WaitUntilAsync(() => connection.State == HubConnectionState.Connected)
            ? connection
            : await ConnectAsync(Roles.Citizen); // the client gave up; a fresh socket shows the same residual
        Assert.Equal(HubConnectionState.Connected, reconnected.State);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + RealtimeTestSupport.ReceiveTimeout;
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(50);
        }

        return condition();
    }
}
