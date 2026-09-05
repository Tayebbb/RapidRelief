using RapidRelief.Client.Common.Realtime;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// Topic-driven refresh. Thirteen pages used to reload on a fixed timer whether or not anything
/// had changed; these tests pin the replacement — refresh on a matching push, never poll while
/// the hub is up, and catch up once after a reconnect.
/// </summary>
public sealed class LiveUpdateServiceTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Immediate = TimeSpan.FromMilliseconds(1);

    private static NotificationDto Note(string topic)
        => new(Guid.NewGuid(), topic, topic, "{}", "All", null, null, Anchor, IsRead: false);

    private static LiveUpdateService Live(NotificationState state)
        => new(state, logger: null, fallbackInterval: TimeSpan.FromMilliseconds(30), coalesceWindow: Immediate);

    private static NotificationState State() => new(new FakeNotificationsApi());

    /// <summary>Waits for the coalesce window plus slack; the handler runs on a detached task.</summary>
    private static async Task<int> SettleAsync(Func<int> read, int expected)
    {
        for (var i = 0; i < 100 && read() < expected; i++)
        {
            await Task.Delay(10);
        }

        return read();
    }

    [Fact]
    public async Task A_push_on_a_subscribed_topic_refreshes_the_page()
    {
        var state = State();
        using var live = Live(state);
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.IncidentReported);

        state.ApplyPush(Note(RealtimeTopics.IncidentReported));

        Assert.Equal(1, await SettleAsync(() => refreshes, 1));
    }

    [Fact]
    public async Task A_push_on_an_unrelated_topic_does_not_refresh_the_page()
    {
        var state = State();
        // Fallback disabled: this test is about topic matching, not about the offline safety tick.
        using var live = new LiveUpdateService(state, logger: null,
            fallbackInterval: TimeSpan.FromMinutes(5), coalesceWindow: Immediate);
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.ReliefStatus);

        state.ApplyPush(Note(RealtimeTopics.IncidentReported));
        await Task.Delay(60);

        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task A_prefix_subscription_covers_topics_added_later()
    {
        var state = State();
        using var live = Live(state);
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; }, "rescue");

        state.ApplyPush(Note(RealtimeTopics.RescueTeamAvailability));

        Assert.Equal(1, await SettleAsync(() => refreshes, 1));
    }

    [Fact]
    public async Task A_burst_of_notifications_causes_one_reload_not_five()
    {
        var state = State();
        using var live = new LiveUpdateService(state, logger: null,
            fallbackInterval: TimeSpan.FromMinutes(5), coalesceWindow: TimeSpan.FromMilliseconds(40));
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.IncidentFeed);

        for (var i = 0; i < 5; i++)
        {
            state.ApplyPush(Note(RealtimeTopics.IncidentReported));
        }

        await SettleAsync(() => refreshes, 1);
        await Task.Delay(60);

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Nothing_ticks_while_the_hub_is_connected()
    {
        var state = State();
        state.SetHubConnected(true);
        using var live = Live(state);
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.IncidentFeed);

        await Task.Delay(120); // several fallback intervals

        Assert.True(live.IsLive);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task The_fallback_tick_only_runs_while_the_hub_is_down()
    {
        var state = State();
        using var live = Live(state);
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.IncidentFeed);

        Assert.True(await SettleAsync(() => refreshes, 1) >= 1);
    }

    [Fact]
    public async Task A_reconnect_refreshes_once_so_the_outage_gap_is_not_missed()
    {
        var state = State();
        using var live = new LiveUpdateService(state, logger: null,
            fallbackInterval: TimeSpan.FromMinutes(5), coalesceWindow: Immediate);
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.IncidentFeed);

        state.SetHubConnected(true);

        Assert.Equal(1, await SettleAsync(() => refreshes, 1));
    }

    [Fact]
    public async Task A_disposed_subscription_stops_receiving_refreshes()
    {
        var state = State();
        using var live = new LiveUpdateService(state, logger: null,
            fallbackInterval: TimeSpan.FromMinutes(5), coalesceWindow: Immediate);
        var refreshes = 0;
        var subscription = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.IncidentFeed);

        subscription.Dispose();
        state.ApplyPush(Note(RealtimeTopics.IncidentReported));
        await Task.Delay(60);

        Assert.Equal(0, refreshes);
        Assert.Equal(0, live.SubscriberCount);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_stop_the_other_subscribers()
    {
        var state = State();
        using var live = Live(state);
        var survived = 0;
        using var _ = live.Subscribe(() => throw new InvalidOperationException("page blew up"),
            RealtimeTopics.IncidentReported);
        using var __ = live.Subscribe(() => { survived++; return Task.CompletedTask; },
            RealtimeTopics.IncidentReported);

        state.ApplyPush(Note(RealtimeTopics.IncidentReported));

        Assert.Equal(1, await SettleAsync(() => survived, 1));
    }

    [Fact]
    public async Task Notifications_that_arrive_by_poll_also_drive_a_refresh()
    {
        var state = State();
        using var live = new LiveUpdateService(state, logger: null,
            fallbackInterval: TimeSpan.FromMinutes(5), coalesceWindow: Immediate);
        var refreshes = 0;
        using var _ = live.Subscribe(() => { refreshes++; return Task.CompletedTask; },
            RealtimeTopics.IncidentReported);

        // The first page is the existing inbox, not news — it must not reload every page on sign-in.
        state.ApplyPage(new NotificationPage([Note(RealtimeTopics.IncidentReported)], Anchor, "c1"));
        await Task.Delay(40);
        Assert.Equal(0, refreshes);

        state.ApplyPage(new NotificationPage([Note(RealtimeTopics.IncidentReported)], Anchor, "c2"));

        Assert.Equal(1, await SettleAsync(() => refreshes, 1));
    }
}

/// <summary>Guards the one place server and client agree on what a topic is called.</summary>
public sealed class RealtimeTopicsTests
{
    [Fact]
    public void An_exact_topic_matches_itself()
        => Assert.True(RealtimeTopics.Matches("rescue.mission.assigned", [RealtimeTopics.RescueMissionAssigned]));

    [Fact]
    public void A_prefix_only_matches_on_a_segment_boundary()
    {
        Assert.True(RealtimeTopics.Matches("rescue.mission.assigned", ["rescue.mission"]));
        Assert.False(RealtimeTopics.Matches("rescuex.mission.assigned", ["rescue"]));
        Assert.False(RealtimeTopics.Matches("incidentsarchive.report", ["incidents"]));
    }

    [Fact]
    public void An_unknown_or_empty_topic_matches_nothing()
    {
        Assert.False(RealtimeTopics.Matches(null, [RealtimeTopics.IncidentReported]));
        Assert.False(RealtimeTopics.Matches("", [RealtimeTopics.IncidentReported]));
        Assert.False(RealtimeTopics.Matches(RealtimeTopics.IncidentReported, []));
    }

    [Fact]
    public void Every_topic_obeys_the_naming_convention_the_server_sanitiser_enforces()
    {
        foreach (var topic in RealtimeTopics.CommandFeed.Distinct())
        {
            Assert.Matches("^[a-z0-9.]{1,64}$", topic);
        }
    }

    [Fact]
    public void The_command_feed_covers_every_operational_topic()
    {
        Assert.Contains(RealtimeTopics.IncidentReported, RealtimeTopics.CommandFeed);
        Assert.Contains(RealtimeTopics.RescueTeamAvailability, RealtimeTopics.CommandFeed);
        Assert.Contains(RealtimeTopics.ReliefStatus, RealtimeTopics.CommandFeed);
        Assert.Contains(RealtimeTopics.AlertPublished, RealtimeTopics.CommandFeed);
    }
}
