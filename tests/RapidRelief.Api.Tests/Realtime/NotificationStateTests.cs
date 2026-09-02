using RapidRelief.Client.Common.Realtime;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// F9 chunk 2 client state. The Client assembly flows here transitively (the Api hosts it),
/// so the merge/dedupe/read rules that the bell, inbox and toasts render are covered by real
/// tests even though there is no bUnit in the closed package list.
/// </summary>
public sealed class NotificationStateTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    private static NotificationDto Note(
        Guid? id = null, string topic = "ai.incident.assessed", int secondsAfterAnchor = 0, bool isRead = false)
        => new(id ?? Guid.NewGuid(), topic, $"summary of {topic}", "{}", "All", null, null,
            Anchor.AddSeconds(secondsAfterAnchor), isRead);

    private static NotificationPage Page(string? cursor, params NotificationDto[] items)
        => new(items, Anchor, cursor);

    [Fact]
    public void A_pushed_notification_lands_in_the_list_and_raises_changed()
    {
        var state = new NotificationState(new FakeNotificationsApi());
        var changed = 0;
        state.Changed += () => changed++;

        state.ApplyPush(Note(topic: "alerts.published"));

        Assert.Equal("alerts.published", Assert.Single(state.Items).Topic);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void A_push_and_a_poll_of_the_same_id_produce_one_entry()
    {
        var id = Guid.NewGuid();
        var api = new FakeNotificationsApi { NextPage = Page("c1", Note(id)) };
        var state = new NotificationState(api);

        state.ApplyPush(Note(id));
        state.ApplyPage(api.NextPage!);

        Assert.Single(state.Items);
        Assert.Equal(1, state.UnreadCount);
    }

    [Fact]
    public void Only_genuinely_new_pushes_raise_the_toast_event()
    {
        var id = Guid.NewGuid();
        var state = new NotificationState(new FakeNotificationsApi());
        var toasts = new List<NotificationDto>();
        state.Pushed += toasts.Add;

        state.ApplyPush(Note(id));
        state.ApplyPush(Note(id));

        Assert.Single(toasts);
    }

    [Fact]
    public void Polled_pages_never_raise_the_toast_event()
    {
        var state = new NotificationState(new FakeNotificationsApi());
        var toasts = new List<NotificationDto>();
        state.Pushed += toasts.Add;

        state.ApplyPage(Page("c1", Note(), Note(secondsAfterAnchor: 1)));

        Assert.Empty(toasts);
    }

    [Fact]
    public void Items_are_newest_first()
    {
        var state = new NotificationState(new FakeNotificationsApi());

        state.ApplyPage(Page("c1",
            Note(topic: "oldest", secondsAfterAnchor: 0),
            Note(topic: "middle", secondsAfterAnchor: 5),
            Note(topic: "newest", secondsAfterAnchor: 9)));

        Assert.Equal(["newest", "middle", "oldest"], state.Items.Select(n => n.Topic));
    }

    [Fact]
    public void Unread_count_ignores_items_the_server_already_marked_read()
    {
        var state = new NotificationState(new FakeNotificationsApi());

        state.ApplyPage(Page("c1", Note(isRead: true), Note(secondsAfterAnchor: 1)));

        Assert.Equal(1, state.UnreadCount);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(4321, "99+")]
    public void Badge_text_is_blank_at_zero_and_caps_at_99_plus(int unread, string expected)
        => Assert.Equal(expected, NotificationState.FormatBadge(unread));

    [Fact]
    public async Task Loading_stores_the_page_and_advances_the_cursor()
    {
        var api = new FakeNotificationsApi { NextPage = Page("cursor-1", Note(), Note(secondsAfterAnchor: 1)) };
        var state = new NotificationState(api);

        await state.LoadAsync();

        Assert.Equal(2, state.Items.Count);
        Assert.Equal("cursor-1", state.Cursor);
        Assert.Null(api.LastSince);
    }

    [Fact]
    public async Task The_next_load_sends_the_stored_cursor()
    {
        var api = new FakeNotificationsApi { NextPage = Page("cursor-1", Note()) };
        var state = new NotificationState(api);

        await state.LoadAsync();
        api.NextPage = Page("cursor-2", Note(secondsAfterAnchor: 1));
        await state.LoadAsync();

        Assert.Equal("cursor-1", api.LastSince);
        Assert.Equal("cursor-2", state.Cursor);
    }

    [Fact]
    public async Task An_unavailable_api_leaves_the_state_untouched_and_never_throws()
    {
        var api = new FakeNotificationsApi { NextPage = Page("cursor-1", Note()) };
        var state = new NotificationState(api);
        await state.LoadAsync();

        api.NextPage = null; // degraded (503), offline, or 401 — all surface as "no page"
        await state.LoadAsync();

        Assert.Single(state.Items);
        Assert.Equal("cursor-1", state.Cursor);
    }

    [Fact]
    public async Task An_item_read_locally_stays_read_when_a_later_page_repeats_it()
    {
        var id = Guid.NewGuid();
        var api = new FakeNotificationsApi { NextPage = Page("c1", Note(id)) };
        var state = new NotificationState(api);
        await state.LoadAsync();
        await state.MarkReadAsync(id);

        state.ApplyPage(Page("c2", Note(id)));

        Assert.True(Assert.Single(state.Items).IsRead);
        Assert.Equal(0, state.UnreadCount);
    }

    [Fact]
    public async Task Marking_read_flips_the_item_and_the_unread_count()
    {
        var id = Guid.NewGuid();
        var state = new NotificationState(new FakeNotificationsApi());
        state.ApplyPush(Note(id));

        var marked = await state.MarkReadAsync(id);

        Assert.True(marked);
        Assert.Equal(0, state.UnreadCount);
    }

    [Fact]
    public async Task A_failed_mark_read_leaves_the_item_unread()
    {
        var id = Guid.NewGuid();
        var api = new FakeNotificationsApi { MarkReadSucceeds = false };
        var state = new NotificationState(api);
        state.ApplyPush(Note(id));

        var marked = await state.MarkReadAsync(id);

        Assert.False(marked);
        Assert.Equal(1, state.UnreadCount);
    }

    [Fact]
    public async Task Mark_all_read_clears_the_badge()
    {
        var api = new FakeNotificationsApi { MarkAllResult = 2 };
        var state = new NotificationState(api);
        state.ApplyPage(Page("c1", Note(), Note(secondsAfterAnchor: 1)));

        var marked = await state.MarkAllReadAsync();

        Assert.Equal(2, marked);
        Assert.Equal(0, state.UnreadCount);
        Assert.All(state.Items, n => Assert.True(n.IsRead));
    }

    [Fact]
    public async Task A_failed_mark_all_read_leaves_the_badge_alone()
    {
        var api = new FakeNotificationsApi { MarkAllResult = null };
        var state = new NotificationState(api);
        state.ApplyPage(Page("c1", Note(), Note(secondsAfterAnchor: 1)));

        var marked = await state.MarkAllReadAsync();

        Assert.Null(marked);
        Assert.Equal(2, state.UnreadCount);
    }

    [Fact]
    public void Clearing_drops_everything_so_the_next_user_starts_empty()
    {
        var state = new NotificationState(new FakeNotificationsApi());
        state.ApplyPage(Page("c1", Note()));

        state.Clear();

        Assert.Empty(state.Items);
        Assert.Equal(0, state.UnreadCount);
        Assert.Null(state.Cursor);
    }

    [Fact]
    public async Task Three_consecutive_unauthorized_fetches_stop_the_polling()
    {
        var api = new FakeNotificationsApi { NextOutcome = NotificationFetchOutcome.Unauthorized };
        var state = new NotificationState(api);

        for (var attempt = 0; attempt < NotificationState.MaxConsecutiveUnauthorized; attempt++)
        {
            await state.LoadAsync();
        }

        Assert.True(state.PollingSuspended);
        Assert.Equal(NotificationState.MaxConsecutiveUnauthorized, api.GetCalls);

        await state.LoadAsync();

        Assert.Equal(NotificationState.MaxConsecutiveUnauthorized, api.GetCalls);
    }

    [Fact]
    public async Task A_successful_fetch_resets_the_unauthorized_streak()
    {
        var api = new FakeNotificationsApi { NextOutcome = NotificationFetchOutcome.Unauthorized };
        var state = new NotificationState(api);
        await state.LoadAsync();
        await state.LoadAsync();

        api.NextOutcome = NotificationFetchOutcome.Ok;
        api.NextPage = Page("c1", Note());
        await state.LoadAsync();
        api.NextOutcome = NotificationFetchOutcome.Unauthorized;
        await state.LoadAsync();
        await state.LoadAsync();

        Assert.False(state.PollingSuspended);
    }

    [Fact]
    public async Task Offline_or_degraded_fetches_never_stop_the_polling_fallback()
    {
        var api = new FakeNotificationsApi { NextOutcome = NotificationFetchOutcome.Failed };
        var state = new NotificationState(api);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await state.LoadAsync();
        }

        Assert.False(state.PollingSuspended);
        Assert.Equal(10, api.GetCalls);
    }

    [Fact]
    public async Task An_identity_change_resumes_polling_after_an_unauthorized_stop()
    {
        var api = new FakeNotificationsApi { NextOutcome = NotificationFetchOutcome.Unauthorized };
        var state = new NotificationState(api);
        for (var attempt = 0; attempt < NotificationState.MaxConsecutiveUnauthorized; attempt++)
        {
            await state.LoadAsync();
        }

        state.Clear();
        api.NextOutcome = NotificationFetchOutcome.Ok;
        api.NextPage = Page("c1", Note());
        await state.LoadAsync();

        Assert.False(state.PollingSuspended);
        Assert.Single(state.Items);
    }
}

/// <summary>In-memory stand-in for the HTTP client — the transport is smoke-tested, not unit-tested.</summary>
internal sealed class FakeNotificationsApi : INotificationsApi
{
    public NotificationPage? NextPage { get; set; }

    public NotificationFetchOutcome NextOutcome { get; set; } = NotificationFetchOutcome.Ok;

    public string? LastSince { get; private set; }

    public int GetCalls { get; private set; }

    public bool MarkReadSucceeds { get; set; } = true;

    public int? MarkAllResult { get; set; } = 0;

    public int? UnreadCount { get; set; }

    public Task<NotificationFetch> GetAsync(string? since, int? limit, CancellationToken ct = default)
    {
        LastSince = since;
        GetCalls++;
        // A missing page can never be an Ok fetch — that is the real client's contract too.
        var outcome = NextOutcome == NotificationFetchOutcome.Ok && NextPage is null
            ? NotificationFetchOutcome.Failed
            : NextOutcome;
        return Task.FromResult(new NotificationFetch(outcome, NextPage));
    }

    public Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(MarkReadSucceeds);

    public Task<int?> MarkAllReadAsync(CancellationToken ct = default) => Task.FromResult(MarkAllResult);

    public Task<int?> GetUnreadCountAsync(CancellationToken ct = default) => Task.FromResult(UnreadCount);
}
