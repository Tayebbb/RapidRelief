using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using RapidRelief.Client.Common.Auth;
using RapidRelief.Client.Features.Auth;

namespace RapidRelief.Client.Common.Realtime;

/// <summary>
/// Owns the SignalR connection to /hubs/notifications and the realtime lifecycle: connect and
/// poll once a caller can authenticate, tear down and forget the inbox when they can't. Every
/// hub failure is swallowed — the polling fallback is what keeps notifications flowing, so a
/// dead hub must never reach the UI as an error.
/// </summary>
public sealed class NotificationHubClient : IAsyncDisposable
{
    internal const string HubPath = "hubs/notifications";
    internal const string PushMethodName = "notification";

    private readonly JwtAuthStateProvider _authState;
    private readonly AuthApi _authApi;
    private readonly DevRoleState _devRoleState;
    private readonly NotificationState _state;
    private readonly Uri _baseAddress;
    private readonly bool _isDevelopment;
    private readonly ILogger<NotificationHubClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HubConnection? _connection;
    private string? _connectedAs;
    private bool _disposed;

    public NotificationHubClient(
        JwtAuthStateProvider authState,
        AuthApi authApi,
        DevRoleState devRoleState,
        NotificationState state,
        Uri baseAddress,
        bool isDevelopment,
        ILogger<NotificationHubClient> logger)
    {
        _authState = authState;
        _authApi = authApi;
        _devRoleState = devRoleState;
        _state = state;
        _baseAddress = baseAddress;
        _isDevelopment = isDevelopment;
        _logger = logger;

        _authState.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _devRoleState.Changed += OnDevRoleChanged;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Brings the connection in line with the current identity: start on login (or dev role),
    /// restart when the identity changes, stop and clear on logout.
    /// </summary>
    public async Task SyncAsync()
    {
        if (_disposed)
        {
            return;
        }

        var hasSession = _authState.HasSession;
        var devRole = _devRoleState.CurrentRole;
        // Keyed by user id, so signing out of A and into B restarts the connection and the
        // inbox instead of silently reusing A's socket.
        var identity = RealtimeConnectionPolicy.IdentityKey(
            hasSession, _authState.UserId, devRole, _isDevelopment);

        await _gate.WaitAsync();
        try
        {
            if (identity == _connectedAs)
            {
                return;
            }

            await StopCoreAsync();
            _state.Clear();
            _connectedAs = identity;
            if (identity is null)
            {
                return;
            }

            await StartCoreAsync(hasSession, devRole);
            _state.StartPolling();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _authState.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _devRoleState.Changed -= OnDevRoleChanged;
        await StopCoreAsync();
        _gate.Dispose();
    }

    private async Task StartCoreAsync(bool hasSession, string devRole)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_baseAddress, HubPath), options =>
            {
                options.AccessTokenProvider = GetAccessTokenAsync;
                if (RealtimeConnectionPolicy.UseDevTransport(hasSession, devRole, _isDevelopment))
                {
                    // D-035: only long polling can carry the dev header through negotiate.
                    options.Transports = HttpTransportType.LongPolling;
                    options.Headers[DevRoleHandler.HeaderName] = devRole;
                }
            })
            .WithAutomaticReconnect(RealtimeConnectionPolicy.ReconnectDelays)
            .Build();

        connection.On<NotificationDto>(PushMethodName, _state.ApplyPush);
        connection.Reconnected += _ =>
        {
            _state.SetHubConnected(true);
            return Task.CompletedTask;
        };
        connection.Reconnecting += _ =>
        {
            _state.SetHubConnected(false);
            return Task.CompletedTask;
        };
        connection.Closed += _ =>
        {
            _state.SetHubConnected(false);
            return Task.CompletedTask;
        };
        _connection = connection;

        try
        {
            await connection.StartAsync();
            _state.SetHubConnected(true);
        }
        catch (Exception ex)
        {
            // Hub off (D-032 PollingOnly/Off), unreachable, or rejected — polling covers it.
            _state.SetHubConnected(false);
            _logger.LogInformation(ex, "Notification hub unavailable — falling back to polling");
        }
    }

    private async Task StopCoreAsync()
    {
        var connection = _connection;
        _connection = null;
        _state.SetHubConnected(false);
        await _state.StopPollingAsync();
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Notification hub stop failed — disposing anyway");
        }

        await connection.DisposeAsync();
    }

    /// <summary>
    /// Runs before every hub HTTP request, including reconnects — reuse AuthApi's single-flight
    /// refresh so a mid-session reconnect never negotiates with an expired token.
    /// </summary>
    private async Task<string?> GetAccessTokenAsync()
    {
        if (!_authState.HasSession)
        {
            return null; // dev-role connection: the X-Dev-Role header is the credential
        }

        await _authApi.TryRefreshAsync();
        return _authState.AccessToken;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> state) => _ = SyncSafelyAsync();

    private void OnDevRoleChanged() => _ = SyncSafelyAsync();

    private async Task SyncSafelyAsync()
    {
        try
        {
            await SyncAsync();
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Notification realtime sync failed — polling continues");
        }
    }
}
