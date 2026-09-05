using Microsoft.Extensions.Logging;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Client.Common.Realtime;

/// <summary>
/// Turns realtime notifications into page refreshes. A page says which topics it cares about and
/// gets a callback when one of them arrives, instead of reloading on a timer regardless of whether
/// anything changed — thirteen pages used to hold their own <c>Timer</c>.
///
/// While the hub is connected there is no polling at all: the push IS the trigger. A slow safety
/// tick only runs while the hub is down, and a reconnect fires every subscriber once so nothing
/// that happened during the outage is missed.
/// </summary>
public sealed class LiveUpdateService : IDisposable
{
    /// <summary>Safety net while the hub is down; the notification poll is what actually detects change.</summary>
    public static readonly TimeSpan DisconnectedFallbackInterval = TimeSpan.FromSeconds(30);

    /// <summary>A burst of notifications for one event must cause one reload, not five.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(400);

    private readonly NotificationState _notifications;
    private readonly ILogger<LiveUpdateService>? _logger;
    private readonly List<Subscription> _subscriptions = [];
    private readonly TimeSpan _fallbackInterval;
    private readonly TimeSpan _coalesceWindow;

    private CancellationTokenSource? _fallbackCts;
    private bool _lastHubConnected;
    private bool _disposed;

    public LiveUpdateService(
        NotificationState notifications,
        ILogger<LiveUpdateService>? logger = null,
        TimeSpan? fallbackInterval = null,
        TimeSpan? coalesceWindow = null)
    {
        _notifications = notifications;
        _logger = logger;
        _fallbackInterval = fallbackInterval ?? DisconnectedFallbackInterval;
        _coalesceWindow = coalesceWindow ?? CoalesceWindow;

        _lastHubConnected = notifications.HubConnected;
        _notifications.Arrived += OnArrived;
        _notifications.Changed += OnStateChanged;
    }

    /// <summary>True while refreshes are driven by pushes rather than by the fallback tick.</summary>
    public bool IsLive => _notifications.HubConnected;

    public int SubscriberCount => _subscriptions.Count;

    /// <summary>
    /// Refresh <paramref name="onChanged"/> whenever one of <paramref name="topics"/> arrives.
    /// A topic may be a whole prefix (<c>"rescue"</c> covers every rescue topic). Dispose the
    /// result in the page's <c>Dispose</c> — an undisposed subscription keeps the page alive.
    /// </summary>
    public IDisposable Subscribe(Func<Task> onChanged, params string[] topics)
    {
        var subscription = new Subscription(this, onChanged, topics ?? []);
        _subscriptions.Add(subscription);
        EnsureFallbackMatchesState();
        return subscription;
    }

    /// <summary>Fires every subscriber now — used after a manual action that the server won't push back.</summary>
    public void RefreshAll()
    {
        foreach (var subscription in _subscriptions.ToArray())
        {
            subscription.Trigger();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifications.Arrived -= OnArrived;
        _notifications.Changed -= OnStateChanged;
        StopFallback();

        foreach (var subscription in _subscriptions.ToArray())
        {
            subscription.Dispose();
        }
    }

    private void OnArrived(NotificationDto notification)
    {
        foreach (var subscription in _subscriptions.ToArray())
        {
            if (subscription.Wants(notification.Topic))
            {
                subscription.Trigger();
            }
        }
    }

    private void OnStateChanged()
    {
        if (_notifications.HubConnected == _lastHubConnected)
        {
            return;
        }

        var reconnected = _notifications.HubConnected && !_lastHubConnected;
        _lastHubConnected = _notifications.HubConnected;
        EnsureFallbackMatchesState();

        if (reconnected)
        {
            // The socket was down: anything that happened in the gap never reached us as a push.
            RefreshAll();
        }
    }

    private void Remove(Subscription subscription)
    {
        _subscriptions.Remove(subscription);
        EnsureFallbackMatchesState();
    }

    private void EnsureFallbackMatchesState()
    {
        var wanted = !_disposed && _subscriptions.Count > 0 && !_notifications.HubConnected;
        if (wanted && _fallbackCts is null)
        {
            _fallbackCts = new CancellationTokenSource();
            _ = FallbackLoopAsync(_fallbackCts.Token);
        }
        else if (!wanted)
        {
            StopFallback();
        }
    }

    private void StopFallback()
    {
        var cts = _fallbackCts;
        _fallbackCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    private async Task FallbackLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_fallbackInterval, ct);
                if (ct.IsCancellationRequested || _notifications.HubConnected)
                {
                    return;
                }

                RefreshAll();
            }
        }
        catch (OperationCanceledException)
        {
            // Hub came back or the last subscriber went away.
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly LiveUpdateService _owner;
        private readonly Func<Task> _onChanged;
        private readonly string[] _topics;
        private CancellationTokenSource? _pending;
        private bool _disposed;

        internal Subscription(LiveUpdateService owner, Func<Task> onChanged, string[] topics)
        {
            _owner = owner;
            _onChanged = onChanged;
            _topics = topics;
        }

        internal bool Wants(string? topic)
            => _topics.Length == 0 || RealtimeTopics.Matches(topic, _topics);

        internal void Trigger()
        {
            if (_disposed)
            {
                return;
            }

            // Restarting the delay collapses a burst into a single refresh at the end of it.
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new CancellationTokenSource();
            _ = RunAsync(_pending.Token);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
            _owner.Remove(this);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(_owner._coalesceWindow, ct);
                if (ct.IsCancellationRequested || _disposed)
                {
                    return;
                }

                await _onChanged();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer trigger, or the page went away.
            }
            catch (Exception ex)
            {
                // A page that throws while refreshing must not kill everyone else's updates.
                _owner._logger?.LogWarning(ex, "Live update handler failed");
            }
        }
    }
}
