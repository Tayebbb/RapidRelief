using RapidRelief.Client.Common.Auth;

namespace RapidRelief.Client.Common.Realtime;

/// <summary>
/// The client-side realtime rules, kept out of <see cref="NotificationHubClient"/> so they are
/// testable without a browser or a live hub. Values mirror the server's Realtime defaults
/// (appsettings.json) — the WASM client has no configuration file to read them from.
/// </summary>
public static class RealtimeConnectionPolicy
{
    /// <summary>Blueprint reconnect schedule: immediate, then 2 s, 10 s, 30 s.</summary>
    public static TimeSpan[] ReconnectDelays =>
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>Realtime:PollSecondsConnected — the hub is live, polling is only a safety net.</summary>
    public static TimeSpan PollWhenHubConnected => TimeSpan.FromSeconds(60);

    /// <summary>Realtime:PollSecondsDisconnected — polling IS the feature (D-032 PollingOnly).</summary>
    public static TimeSpan PollWhenHubDisconnected => TimeSpan.FromSeconds(5);

    public static TimeSpan PollInterval(bool hubConnected)
        => hubConnected ? PollWhenHubConnected : PollWhenHubDisconnected;

    /// <summary>
    /// D-035: a dev role only survives negotiate as an HTTP header, and browser WebSockets
    /// cannot carry headers — so dev sessions are long-polling. A real JWT always wins.
    /// </summary>
    public static bool UseDevTransport(bool hasSession, string? devRole, bool isDevelopment)
        => !hasSession && HasDevRole(devRole, isDevelopment);

    /// <summary>
    /// Anonymous clients must not connect or poll — every call would be a 401. The dev-role
    /// path only exists in Development: a published build's server ignores X-Dev-Role, so
    /// honouring it there would make every anonymous visitor poll forever against 401s.
    /// </summary>
    public static bool ShouldConnect(bool hasSession, string? devRole, bool isDevelopment)
        => hasSession || HasDevRole(devRole, isDevelopment);

    /// <summary>
    /// The connection identity. A different value means a different principal, so the hub
    /// client must stop, clear the inbox and reconnect — user B never inherits user A's
    /// socket or notifications. Null means "do not connect at all".
    /// </summary>
    public static string? IdentityKey(bool hasSession, string? userId, string? devRole, bool isDevelopment)
    {
        if (!ShouldConnect(hasSession, devRole, isDevelopment))
        {
            return null;
        }

        return hasSession ? $"jwt:{userId ?? "unknown"}" : $"dev:{devRole}";
    }

    private static bool HasDevRole(string? devRole, bool isDevelopment)
        => isDevelopment && !string.IsNullOrWhiteSpace(devRole) && devRole != DevRoleState.None;
}
