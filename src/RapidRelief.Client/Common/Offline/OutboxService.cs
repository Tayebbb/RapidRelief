using System.Text.Json;
using Microsoft.JSInterop;
using RapidRelief.Client.Features.Reports;

namespace RapidRelief.Client.Common.Offline;

public enum OutboxItemState
{
    /// <summary>Saved on this device, waiting for a connection.</summary>
    WaitingToSync,

    /// <summary>The server rejected it — it needs the citizen to fix something, or to call 999.</summary>
    Failed,

    /// <summary>The stored payload could not be read back. Kept, never deleted, always surfaced.</summary>
    Corrupted,
}

/// <summary>The one connectivity/sync state the whole UI renders from.</summary>
public enum ConnectivityState
{
    Online,
    Offline,
    SavedLocally,
    Syncing,
    Synced,
    SyncFailed,
}

public sealed record OutboxItem(
    string Id,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc,
    string State,
    string? Error,
    int Attempts = 0,
    DateTimeOffset? NextAttemptUtc = null,
    DateTimeOffset? LastAttemptUtc = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxItemState ItemState => Enum.TryParse<OutboxItemState>(State, ignoreCase: true, out var parsed)
        ? parsed
        : OutboxItemState.WaitingToSync;

    /// <summary>True when retrying is pointless until the citizen does something first.</summary>
    public bool NeedsAttention => ItemState is OutboxItemState.Failed or OutboxItemState.Corrupted;

    public CreateIncidentRequest? Payload()
    {
        try
        {
            return JsonSerializer.Deserialize<CreateIncidentRequest>(PayloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <param name="Synced">Accepted by the server on this pass.</param>
/// <param name="Failed">Rejected outright, out of retries, or unreadable — these need a human.</param>
/// <param name="Deferred">Still queued: a transport fault, or waiting out a backoff.</param>
/// <param name="Remaining">Everything still on this device after the pass.</param>
public sealed record OutboxSyncResult(int Synced, int Failed, int Deferred, int Remaining)
{
    /// <summary>Some reports landed and some did not — the case that must not read as success.</summary>
    public bool Partial => Synced > 0 && Remaining > 0;
}

/// <summary>
/// Store-and-forward for emergency reports. A report is persisted to IndexedDB before the first
/// network attempt and only dropped once the server has confirmed receipt. Nothing here throws,
/// and nothing here deletes a report the server has not accepted — a silently lost emergency
/// report is the one failure mode this class exists to prevent.
///
/// Delivery is idempotent: the queue id IS the request's idempotency key, so replaying after a
/// timeout the server actually honoured returns the original incident instead of duplicating it.
/// </summary>
public sealed class OutboxService : IAsyncDisposable
{
    private const string ModulePath = "./js/offline-queue.js";

    /// <summary>
    /// Cheap, anonymous, and answers even in degraded mode — a pure "can I reach the server" ping.
    /// Public so a test can prove the API really serves it: a wrong path here would make the app
    /// believe it is permanently offline.
    /// </summary>
    public const string ProbePath = "health";

    /// <summary>After this many transport failures a report stops retrying and asks for a human.</summary>
    public const int MaxAttempts = 8;

    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    ];

    private readonly IJSRuntime _js;
    private readonly IncidentsClient _incidents;
    private readonly TimeProvider _clock;
    private IJSObjectReference? _module;
    private DotNetObjectReference<OutboxService>? _selfRef;
    private bool _syncing;
    private bool _syncFailed;
    private bool _everSynced;

    public OutboxService(IJSRuntime js, IncidentsClient incidents, TimeProvider? clock = null)
    {
        _js = js;
        _incidents = incidents;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>True unless the browser reports the device offline, or a probe proved otherwise.</summary>
    public bool IsOnline { get; private set; } = true;

    /// <summary>Reports still on their way — excludes anything waiting on the citizen.</summary>
    public int PendingCount { get; private set; }

    /// <summary>Queued reports that will not move without help (rejected or unreadable).</summary>
    public int AttentionCount { get; private set; }

    /// <summary>Everything still held on this device, whatever state it is in.</summary>
    public int QueuedCount => PendingCount + AttentionCount;

    public DateTimeOffset? LastSyncedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Set when this device's local queue had to be rebuilt and its contents were lost.</summary>
    public bool LocalStoreWasReset { get; private set; }

    /// <summary>Raised on connectivity change, queue change and sync completion.</summary>
    public event Action? Changed;

    /// <summary>The single state the connectivity badge renders. Order matters: worst news first.</summary>
    public ConnectivityState Status
    {
        get
        {
            if (AttentionCount > 0 || _syncFailed)
            {
                return ConnectivityState.SyncFailed;
            }

            if (_syncing)
            {
                return ConnectivityState.Syncing;
            }

            if (!IsOnline)
            {
                return PendingCount > 0 ? ConnectivityState.SavedLocally : ConnectivityState.Offline;
            }

            if (PendingCount > 0)
            {
                return ConnectivityState.SavedLocally;
            }

            return _everSynced ? ConnectivityState.Synced : ConnectivityState.Online;
        }
    }

    public async Task InitializeAsync()
    {
        var module = await LoadModuleAsync();
        if (module is null)
        {
            return;
        }

        _selfRef ??= DotNetObjectReference.Create(this);
        try
        {
            IsOnline = await module.InvokeAsync<bool>("registerConnectivity", _selfRef);
            LocalStoreWasReset = await module.InvokeAsync<bool>("storeWasRebuilt");
        }
        catch (JSException)
        {
            IsOnline = true;
        }

        await RefreshCountsAsync();
        if (IsOnline && PendingCount > 0)
        {
            await SyncAsync();
        }
    }

    [JSInvokable]
    public async Task OnConnectivityChanged(bool online)
    {
        IsOnline = online;
        if (!online)
        {
            // Coming back is what clears this; a failure badge while offline is just noise.
            _syncFailed = false;
        }

        Changed?.Invoke();

        if (online)
        {
            await SyncAsync();
        }
    }

    /// <summary>
    /// Confirms the server is actually reachable, not just that a network interface exists —
    /// a captive portal and a dead uplink both report "online".
    /// </summary>
    public async Task<bool> CheckReachabilityAsync()
    {
        var module = await LoadModuleAsync();
        if (module is null)
        {
            return IsOnline;
        }

        bool reachable;
        try
        {
            reachable = await module.InvokeAsync<bool>("probe", ProbePath, 4000);
        }
        catch (JSException)
        {
            return IsOnline;
        }

        if (reachable != IsOnline)
        {
            IsOnline = reachable;
            Changed?.Invoke();
        }

        return reachable;
    }

    /// <summary>
    /// Persists a report locally. Returns false only when this device cannot store it at all —
    /// the one case where the citizen must be told to call 999 instead.
    /// </summary>
    public async Task<bool> EnqueueAsync(CreateIncidentRequest request)
    {
        var module = await LoadModuleAsync();
        if (module is null)
        {
            return false;
        }

        // The idempotency key IS the row key, so pressing submit twice updates one row and the
        // server still records one report.
        var id = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey!;

        var now = _clock.GetUtcNow();
        var item = new OutboxItem(
            id,
            JsonSerializer.Serialize(request, OutboxItem.JsonOptions),
            now,
            nameof(OutboxItemState.WaitingToSync),
            Error: null,
            Attempts: 0,
            NextAttemptUtc: now);

        try
        {
            var saved = await module.InvokeAsync<bool>("save", item);
            await RefreshCountsAsync();
            return saved;
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<OutboxItem>> GetItemsAsync()
    {
        var module = await LoadModuleAsync();
        if (module is null)
        {
            return [];
        }

        try
        {
            var items = await module.InvokeAsync<OutboxItem[]>("list");
            return items.OrderBy(x => x.CreatedAtUtc).ToList();
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            // The rows are unreadable from .NET. They stay on disk; the badge says so.
            return [];
        }
    }

    /// <summary>
    /// Drains the queue. Server rejections stop retrying, transport faults back off and stay
    /// queued, and an unreadable payload is quarantined rather than dropped. A run that delivers
    /// some reports and defers others is a partial sync, and is reported as one.
    /// </summary>
    public async Task<OutboxSyncResult> SyncAsync(bool force = false)
    {
        if (_syncing)
        {
            // A second drain would replay rows the first one is mid-flight on.
            return new OutboxSyncResult(0, 0, 0, QueuedCount);
        }

        var module = await LoadModuleAsync();
        if (module is null)
        {
            return new OutboxSyncResult(0, 0, 0, QueuedCount);
        }

        _syncing = true;
        _syncFailed = false;
        Changed?.Invoke();

        var synced = 0;
        var failed = 0;
        var deferred = 0;

        try
        {
            var now = _clock.GetUtcNow();
            foreach (var item in await GetItemsAsync())
            {
                if (item.NeedsAttention)
                {
                    failed++;
                    continue;
                }

                if (!force && item.NextAttemptUtc is { } due && due > now)
                {
                    deferred++;
                    continue;
                }

                var payload = item.Payload();
                if (payload is null)
                {
                    // Never delete: the citizen filed this, and the raw text is still on the device.
                    await QuarantineAsync(module, item,
                        "This report was saved but its contents could not be read back.");
                    failed++;
                    continue;
                }

                var result = await _incidents.CreateAsync(payload);
                if (result.Ok)
                {
                    await module.InvokeVoidAsync("remove", item.Id);
                    synced++;
                    _everSynced = true;
                    LastSyncedAtUtc = _clock.GetUtcNow();
                    continue;
                }

                if (result.FieldErrors is { Count: > 0 })
                {
                    // The server will never accept this payload — keep it, but stop retrying.
                    await SaveAsync(module, item with
                    {
                        State = nameof(OutboxItemState.Failed),
                        Error = result.Error,
                        Attempts = item.Attempts + 1,
                        LastAttemptUtc = _clock.GetUtcNow(),
                        NextAttemptUtc = null,
                    });
                    failed++;
                    LastError = result.Error;
                    continue;
                }

                var attempts = item.Attempts + 1;
                LastError = result.Error;
                _syncFailed = true;

                if (attempts >= MaxAttempts)
                {
                    await SaveAsync(module, item with
                    {
                        State = nameof(OutboxItemState.Failed),
                        Error = $"Could not be delivered after {attempts} attempts. {result.Error}".Trim(),
                        Attempts = attempts,
                        LastAttemptUtc = _clock.GetUtcNow(),
                        NextAttemptUtc = null,
                    });
                    failed++;
                    continue;
                }

                await SaveAsync(module, item with
                {
                    State = nameof(OutboxItemState.WaitingToSync),
                    Error = result.Error,
                    Attempts = attempts,
                    LastAttemptUtc = _clock.GetUtcNow(),
                    NextAttemptUtc = _clock.GetUtcNow() + DelayFor(attempts),
                });
                deferred++;

                // If the server is gone there is no point walking the rest of the queue now —
                // the reconnect handler runs the whole drain again.
                if (!await CheckReachabilityAsync())
                {
                    break;
                }
            }
        }
        finally
        {
            _syncing = false;
            await RefreshCountsAsync();
        }

        return new OutboxSyncResult(synced, failed, deferred, QueuedCount);
    }

    /// <summary>Re-arms a report the citizen has chosen to try again, clearing its backoff.</summary>
    public async Task RetryAsync(string id)
    {
        var module = await LoadModuleAsync();
        if (module is null)
        {
            return;
        }

        var item = (await GetItemsAsync()).FirstOrDefault(x => x.Id == id);
        if (item is null || item.ItemState == OutboxItemState.Corrupted)
        {
            return;
        }

        await SaveAsync(module, item with
        {
            State = nameof(OutboxItemState.WaitingToSync),
            Attempts = 0,
            NextAttemptUtc = _clock.GetUtcNow(),
            Error = null,
        });

        await SyncAsync(force: true);
    }

    /// <summary>Only ever reached from an explicit "discard" the citizen chose.</summary>
    public async Task DiscardAsync(string id)
    {
        var module = await LoadModuleAsync();
        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("remove", id);
        }
        catch (JSException)
        {
            // Nothing to do — the item stays and the citizen can retry.
        }

        await RefreshCountsAsync();
    }

    /// <summary>Backoff schedule, flattening at the last step rather than growing forever.</summary>
    public static TimeSpan DelayFor(int attempts)
        => Backoff[Math.Clamp(attempts, 0, Backoff.Length - 1)];

    private static Task SaveAsync(IJSObjectReference module, OutboxItem item)
        => module.InvokeVoidAsync("save", item).AsTask();

    private static async Task QuarantineAsync(IJSObjectReference module, OutboxItem item, string error)
    {
        try
        {
            await SaveAsync(module, item with
            {
                State = nameof(OutboxItemState.Corrupted),
                Error = error,
                NextAttemptUtc = null,
            });
        }
        catch (JSException)
        {
            // The row keeps its previous state; it is still on the device and still counted.
        }
    }

    private async Task RefreshCountsAsync()
    {
        var items = await GetItemsAsync();
        PendingCount = items.Count(x => !x.NeedsAttention);
        AttentionCount = items.Count(x => x.NeedsAttention);
        Changed?.Invoke();
    }

    private async Task<IJSObjectReference?> LoadModuleAsync()
    {
        if (_module is not null)
        {
            return _module;
        }

        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return _module;
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("unregisterConnectivity");
                await _module.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
            {
                // Page torn down — nothing to clean up on the JS side.
            }
        }
    }
}
