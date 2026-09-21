using System.Globalization;
using Microsoft.Extensions.Logging;

namespace RapidRelief.Client.Common.Realtime;

/// <summary>
/// The single notification store behind the bell, the inbox and the toasts. Push (hub) and poll
/// (HTTP) feed the same dictionary and are deduped by id, so a notification that arrives twice
/// is rendered once and counted once.
/// </summary>
public sealed class NotificationState
{
    private const int BadgeCap = 99;

    /// <summary>A caller the server keeps rejecting must stop asking instead of polling forever.</summary>
    public const int MaxConsecutiveUnauthorized = 3;

    private readonly INotificationsApi _api;
    private readonly ILogger<NotificationState>? _logger;
    private readonly Dictionary<Guid, NotificationDto> _byId = [];

    private List<NotificationDto> _ordered = [];
    private CancellationTokenSource? _pollCts;
    private Task? _pollLoop;
    private int _unauthorizedStreak;
    private bool _primed;

    public NotificationState(INotificationsApi api, ILogger<NotificationState>? logger = null)
    {
        _api = api;
        _logger = logger;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<NotificationDto> Items => _ordered;

    public int UnreadCount { get; private set; }

    public string BadgeText => FormatBadge(UnreadCount);

    /// <summary>Latest server cursor (D-038); the next poll asks for everything after it.</summary>
    public string? Cursor { get; private set; }

    public bool HubConnected { get; private set; }

    /// <summary>Set after <see cref="MaxConsecutiveUnauthorized"/> 401s; cleared by an identity change.</summary>
    public bool PollingSuspended { get; private set; }

    /// <summary>Any state change worth a re-render.</summary>
    public event Action? Changed;

    /// <summary>Live arrivals only — toasts must not replay the inbox on every poll.</summary>
    public event Action<NotificationDto>? Pushed;

    /// <summary>
    /// Every genuinely-new notification, from the hub or from a poll. The first page after a
    /// clear is suppressed: that one is the existing inbox, not news, and replaying it would
    /// make every page reload on sign-in.
    /// </summary>
    public event Action<NotificationDto>? Arrived;

    /// <summary>D-039 display rule: nothing at zero, "99+" above the cap.</summary>
    public static string FormatBadge(int unread) => unread switch
    {
        <= 0 => string.Empty,
        > BadgeCap => $"{BadgeCap}+",
        _ => unread.ToString(CultureInfo.InvariantCulture),
    };

    public void ApplyPush(NotificationDto notification)
    {
        var isNew = Merge(notification);
        Reindex();
        if (isNew)
        {
            Pushed?.Invoke(_byId[notification.Id]);
            Arrived?.Invoke(_byId[notification.Id]);
        }

        _primed = true;
        Changed?.Invoke();
    }

    public void ApplyPage(NotificationPage page)
    {
        List<NotificationDto>? arrivals = null;
        foreach (var notification in page.Items)
        {
            if (Merge(notification) && _primed)
            {
                (arrivals ??= []).Add(notification);
            }
        }

        if (!string.IsNullOrEmpty(page.NextCursor))
        {
            Cursor = page.NextCursor;
        }

        _primed = true;
        Reindex();

        if (arrivals is not null)
        {
            foreach (var arrival in arrivals)
            {
                Arrived?.Invoke(arrival);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>One incremental fetch. A missing page (offline/degraded) changes nothing.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (PollingSuspended)
        {
            return;
        }

        var fetch = await _api.GetAsync(Cursor, limit: null, ct);
        if (fetch.Outcome == NotificationFetchOutcome.Unauthorized)
        {
            if (++_unauthorizedStreak >= MaxConsecutiveUnauthorized)
            {
                PollingSuspended = true;
                _logger?.LogWarning(
                    "Notification polling stopped after {Count} consecutive 401s — it resumes on the next sign-in",
                    _unauthorizedStreak);
            }

            return;
        }

        _unauthorizedStreak = 0;
        if (fetch.Page is not null)
        {
            ApplyPage(fetch.Page);
        }
    }

    public async Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        if (!await _api.MarkReadAsync(id, ct))
        {
            return false;
        }

        if (_byId.TryGetValue(id, out var notification))
        {
            _byId[id] = notification with { IsRead = true };
            Reindex();
            Changed?.Invoke();
        }

        return true;
    }

    /// <summary>Returns the server's marked count, or null when the call did not succeed.</summary>
    public async Task<int?> MarkAllReadAsync(CancellationToken ct = default)
    {
        var marked = await _api.MarkAllReadAsync(ct);
        if (marked is null)
        {
            return null;
        }

        foreach (var id in _byId.Keys.ToList())
        {
            _byId[id] = _byId[id] with { IsRead = true };
        }

        Reindex();
        Changed?.Invoke();
        return marked;
    }

    public void SetHubConnected(bool connected)
    {
        if (HubConnected == connected)
        {
            return;
        }

        HubConnected = connected;
        Changed?.Invoke();
    }

    /// <summary>Logout or dev-role switch: the next identity must not inherit this inbox.</summary>
    public void Clear()
    {
        _byId.Clear();
        Cursor = null;
        _unauthorizedStreak = 0;
        PollingSuspended = false;
        _primed = false;
        Reindex();
        Changed?.Invoke();
    }

    public void StartPolling()
    {
        if (_pollLoop is { IsCompleted: false })
        {
            return;
        }

        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        _pollLoop = PollAsync(_pollCts.Token);
    }

    public async Task StopPollingAsync()
    {
        var loop = _pollLoop;
        _pollLoop = null;
        if (_pollCts is not null)
        {
            await _pollCts.CancelAsync();
            _pollCts.Dispose();
            _pollCts = null;
        }

        if (loop is not null)
        {
            await loop;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !PollingSuspended)
        {
            try
            {
                await LoadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // A failed tick is never fatal: polling is the fallback that must outlive faults.
            }

            try
            {
                await Task.Delay(RealtimeConnectionPolicy.PollInterval(HubConnected), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Returns true when the id was not already known.</summary>
    private bool Merge(NotificationDto incoming)
    {
        if (!_byId.TryGetValue(incoming.Id, out var existing))
        {
            _byId[incoming.Id] = incoming;
            return true;
        }

        // A page fetched before a local mark-read must not resurrect the unread state.
        _byId[incoming.Id] = incoming with { IsRead = incoming.IsRead || existing.IsRead };
        return false;
    }

    private void Reindex()
    {
        _ordered = _byId.Values
            .OrderByDescending(n => n.CreatedAtUtc)
            .ThenByDescending(n => n.Id)
            .ToList();
        UnreadCount = _ordered.Count(n => !n.IsRead);
    }
}
