using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using RapidRelief.Client.Common.Offline;
using RapidRelief.Client.Features.Reports;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Offline;

/// <summary>
/// The store-and-forward queue behind offline reporting. The rule these tests exist to hold is
/// blunt: an emergency report is never dropped silently. Everything else — retry, backoff,
/// partial sync, duplicate suppression, quarantine — is in service of it.
/// </summary>
public sealed class OutboxServiceTests
{
    private static CreateIncidentRequest Request(string? key = "key-1") => new(
        "Flood", "Water rising fast", DisasterType.Flood, Severity.Severe,
        23.8103, 90.4125, "Mirpur", 4, IsSos: false, ContactPhone: "0100", PhotoPaths: null,
        IdempotencyKey: key);

    private static (OutboxService Outbox, FakeOutboxStore Store, StubIncidentApi Api) Build()
    {
        var store = new FakeOutboxStore();
        var api = new StubIncidentApi();
        var outbox = new OutboxService(new FakeJsRuntime(store), new IncidentsClient(api.Client));
        return (outbox, store, api);
    }

    [Fact]
    public async Task A_report_is_on_the_device_before_any_network_call_is_made()
    {
        var (outbox, store, api) = Build();

        Assert.True(await outbox.EnqueueAsync(Request()));

        Assert.Single(store.Rows);
        Assert.Equal(0, api.Calls);
        Assert.Equal(1, outbox.PendingCount);
        Assert.Equal(ConnectivityState.SavedLocally, outbox.Status);
    }

    [Fact]
    public async Task Submitting_the_same_report_twice_leaves_one_row_because_the_key_is_the_row_id()
    {
        var (outbox, store, _) = Build();

        await outbox.EnqueueAsync(Request());
        await outbox.EnqueueAsync(Request());

        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task A_delivered_report_leaves_the_queue_and_the_badge_says_synced()
    {
        var (outbox, store, api) = Build();
        await outbox.EnqueueAsync(Request());

        var result = await outbox.SyncAsync();

        Assert.Equal(1, result.Synced);
        Assert.Empty(store.Rows);
        Assert.Equal(1, api.Calls);
        Assert.Equal(ConnectivityState.Synced, outbox.Status);
    }

    [Fact]
    public async Task A_transport_failure_keeps_the_report_queued_and_schedules_a_retry()
    {
        var (outbox, store, api) = Build();
        api.Mode = StubMode.Unreachable;
        await outbox.EnqueueAsync(Request());

        var result = await outbox.SyncAsync();

        Assert.Equal(0, result.Synced);
        Assert.Equal(1, result.Deferred);
        var row = Assert.Single(store.Rows).Value;
        Assert.Equal(OutboxItemState.WaitingToSync, row.ItemState);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.NextAttemptUtc);
        Assert.Equal(ConnectivityState.SyncFailed, outbox.Status);
    }

    [Fact]
    public async Task A_server_rejection_stops_retrying_but_never_deletes_the_report()
    {
        var (outbox, store, api) = Build();
        await outbox.EnqueueAsync(Request());

        // A payload the server will never take must stop consuming retries, and still be visible.
        api.Mode = StubMode.Rejected;
        var result = await outbox.SyncAsync();

        Assert.Equal(1, result.Failed);
        var row = Assert.Single(store.Rows).Value;
        Assert.Equal(OutboxItemState.Failed, row.ItemState);
        Assert.Null(row.NextAttemptUtc);
        Assert.Equal(1, outbox.AttentionCount);
        Assert.Equal(0, outbox.PendingCount);
    }

    [Fact]
    public async Task An_unreadable_payload_is_quarantined_rather_than_dropped()
    {
        var (outbox, store, _) = Build();
        await outbox.EnqueueAsync(Request());
        store.Corrupt("key-1");

        var result = await outbox.SyncAsync();

        Assert.Equal(1, result.Failed);
        var row = Assert.Single(store.Rows).Value;
        Assert.Equal(OutboxItemState.Corrupted, row.ItemState);
        Assert.Equal(1, outbox.AttentionCount);
    }

    [Fact]
    public async Task A_report_that_never_lands_gives_up_after_the_attempt_cap_and_asks_for_a_human()
    {
        var (outbox, store, api) = Build();
        api.Mode = StubMode.Unreachable;
        await outbox.EnqueueAsync(Request());

        for (var i = 0; i < OutboxService.MaxAttempts; i++)
        {
            await outbox.SyncAsync(force: true);
        }

        var row = Assert.Single(store.Rows).Value;
        Assert.Equal(OutboxItemState.Failed, row.ItemState);
        Assert.Contains("attempts", row.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectivityState.SyncFailed, outbox.Status);
    }

    [Fact]
    public async Task A_queued_report_is_skipped_until_its_backoff_has_elapsed()
    {
        var (outbox, store, api) = Build();
        api.Mode = StubMode.Unreachable;
        await outbox.EnqueueAsync(Request());
        await outbox.SyncAsync();
        api.Mode = StubMode.Accepted;
        api.Calls = 0;

        var deferredRun = await outbox.SyncAsync();

        Assert.Equal(1, deferredRun.Deferred);
        Assert.Equal(0, api.Calls);
        Assert.Single(store.Rows);

        // "Sync now" and reconnect both ignore the backoff — the citizen asked.
        var forced = await outbox.SyncAsync(force: true);

        Assert.Equal(1, forced.Synced);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task A_run_that_delivers_some_and_defers_others_reports_itself_as_partial()
    {
        var (outbox, store, api) = Build();
        await outbox.EnqueueAsync(Request("good"));
        await outbox.EnqueueAsync(Request("bad"));
        api.Mode = StubMode.RejectSpecific;
        api.RejectKey = "bad";

        var result = await outbox.SyncAsync();

        Assert.Equal(1, result.Synced);
        Assert.Equal(1, result.Failed);
        Assert.True(result.Partial);
        Assert.Single(store.Rows);
        Assert.Equal("bad", store.Rows.Single().Key);
    }

    [Fact]
    public async Task Reconnecting_drains_the_queue_without_being_asked()
    {
        var (outbox, store, _) = Build();
        await outbox.EnqueueAsync(Request());

        await outbox.OnConnectivityChanged(online: true);

        Assert.Empty(store.Rows);
        Assert.Equal(0, outbox.PendingCount);
    }

    [Fact]
    public async Task Going_offline_is_reported_as_saved_locally_not_as_a_failure()
    {
        var (outbox, _, api) = Build();
        api.Mode = StubMode.Unreachable;
        await outbox.EnqueueAsync(Request());
        await outbox.SyncAsync();
        Assert.Equal(ConnectivityState.SyncFailed, outbox.Status);

        await outbox.OnConnectivityChanged(online: false);

        Assert.Equal(ConnectivityState.SavedLocally, outbox.Status);
    }

    [Fact]
    public async Task Retrying_a_stuck_report_clears_its_backoff_and_sends_it()
    {
        var (outbox, store, api) = Build();
        api.Mode = StubMode.Unreachable;
        await outbox.EnqueueAsync(Request());
        await outbox.SyncAsync();
        api.Mode = StubMode.Accepted;

        await outbox.RetryAsync("key-1");

        Assert.Empty(store.Rows);
        Assert.Equal(0, outbox.AttentionCount);
    }

    [Fact]
    public async Task A_device_that_cannot_store_anything_says_so_instead_of_pretending_to_save()
    {
        var store = new FakeOutboxStore { WritesFail = true };
        var api = new StubIncidentApi();
        var outbox = new OutboxService(new FakeJsRuntime(store), new IncidentsClient(api.Client));

        Assert.False(await outbox.EnqueueAsync(Request()));
    }

    [Fact]
    public void The_retry_delay_grows_and_then_flattens_instead_of_running_away()
    {
        Assert.Equal(TimeSpan.Zero, OutboxService.DelayFor(0));
        Assert.True(OutboxService.DelayFor(3) > OutboxService.DelayFor(1));
        Assert.Equal(OutboxService.DelayFor(20), OutboxService.DelayFor(OutboxService.MaxAttempts));
    }
}

/// <summary>
/// The reachability probe decides whether the app believes it is online. A path the API does not
/// serve would pin the whole client to "offline" forever, so the route is pinned against the real
/// composition rather than trusted.
/// </summary>
public sealed class OutboxProbeRouteTests(TestingWebAppFactory factory) : IClassFixture<TestingWebAppFactory>
{
    [Fact]
    public async Task The_probe_path_is_a_route_the_api_actually_serves_anonymously()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(OutboxService.ProbePath);

        Assert.True(response.IsSuccessStatusCode,
            $"GET /{OutboxService.ProbePath} returned {(int)response.StatusCode} — the offline probe would " +
            "report the server unreachable even when it is up.");
    }
}

internal enum StubMode
{
    Accepted,
    Rejected,
    Unreachable,
    RejectSpecific,
}

/// <summary>Stands in for the incidents endpoint so the queue can be driven through every outcome.</summary>
internal sealed class StubIncidentApi
{
    public StubIncidentApi()
    {
        Client = new HttpClient(new Handler(this)) { BaseAddress = new Uri("https://localhost/") };
    }

    public HttpClient Client { get; }

    public StubMode Mode { get; set; } = StubMode.Accepted;

    public string? RejectKey { get; set; }

    public int Calls { get; set; }

    private sealed class Handler(StubIncidentApi owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            owner.Calls++;
            var body = await request.Content!.ReadAsStringAsync(ct);
            var mode = owner.Mode;
            if (mode == StubMode.RejectSpecific)
            {
                mode = owner.RejectKey is not null && body.Contains(owner.RejectKey, StringComparison.Ordinal)
                    ? StubMode.Rejected
                    : StubMode.Accepted;
            }

            return mode switch
            {
                StubMode.Unreachable => throw new HttpRequestException("no route to host"),
                StubMode.Rejected => Problem(),
                _ => Accepted(),
            };
        }

        private static HttpResponseMessage Accepted()
        {
            var incident = new IncidentDto(
                Guid.NewGuid(), Guid.NewGuid(), "Flood", "Water rising fast", DisasterType.Flood,
                Severity.Severe, IncidentStatus.Reported, new GeoPoint(23.8103, 90.4125), "Mirpur", 4,
                false, null, null, string.Empty, null, null, null, null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, [], []);

            return Json(HttpStatusCode.Created,
                JsonSerializer.Serialize(new ApiEnvelope<IncidentDto>(incident),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        private static HttpResponseMessage Problem() => Json(HttpStatusCode.BadRequest,
            """{"title":"Validation failed","status":400,"errors":{"Description":["Too short."]}}""");

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>In-memory stand-in for the IndexedDB module, including a way to damage a stored row.</summary>
internal sealed class FakeOutboxStore
{
    public Dictionary<string, OutboxItem> Rows { get; } = [];

    public bool WritesFail { get; set; }

    /// <summary>Simulates a row whose payload can no longer be deserialized.</summary>
    public void Corrupt(string id)
    {
        if (Rows.TryGetValue(id, out var row))
        {
            Rows[id] = row with { PayloadJson = "{not json" };
        }
    }

    public bool Save(OutboxItem item)
    {
        if (WritesFail)
        {
            return false;
        }

        Rows[item.Id] = item;
        return true;
    }

    public bool Remove(string id) => Rows.Remove(id);
}

/// <summary>Routes the outbox's JS calls at <see cref="FakeOutboxStore"/> instead of a browser.</summary>
internal sealed class FakeJsRuntime(FakeOutboxStore store) : IJSRuntime
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => Invoke<TValue>(identifier, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        => Invoke<TValue>(identifier, args);

    private ValueTask<TValue> Invoke<TValue>(string identifier, object?[]? args)
    {
        if (identifier == "import")
        {
            return ValueTask.FromResult((TValue)(object)new Module(store));
        }

        throw new NotSupportedException(identifier);
    }

    private sealed class Module(FakeOutboxStore store) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            object? result = identifier switch
            {
                "list" => store.Rows.Values.ToArray(),
                "save" => store.Save(Rehydrate(args![0])),
                "remove" => store.Remove((string)args![0]!),
                "registerConnectivity" => true,
                "storeWasRebuilt" => false,
                // The queue asks whether the server is really there; the stub says yes so the
                // drain walks the whole queue and partial-sync behaviour is exercised.
                "probe" => true,
                "unregisterConnectivity" => (object?)null,
                _ => throw new NotSupportedException(identifier),
            };

            return ValueTask.FromResult(result is TValue typed ? typed : default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>Round-trips through JSON exactly as the real interop boundary would.</summary>
        private static OutboxItem Rehydrate(object? argument)
            => argument as OutboxItem
               ?? JsonSerializer.Deserialize<OutboxItem>(JsonSerializer.Serialize(argument, Options), Options)!;
    }
}
