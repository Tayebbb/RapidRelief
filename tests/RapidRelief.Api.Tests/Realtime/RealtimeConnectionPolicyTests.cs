using RapidRelief.Client.Common.Auth;
using RapidRelief.Client.Common.Realtime;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// The client-side connection rules that decide whether to talk to the hub at all, which
/// transport to use (D-035) and how often to poll while disconnected. The dev-role path is
/// Development-only: outside it an anonymous visitor must stay completely silent.
/// </summary>
public sealed class RealtimeConnectionPolicyTests
{
    [Theory]
    [InlineData(false, Roles.Admin, true, true)]    // dev-role session: LongPolling can carry X-Dev-Role
    [InlineData(false, Roles.Admin, false, false)]  // published build: there is no dev role at all
    [InlineData(true, Roles.Admin, true, false)]    // real JWT wins — default transports (WebSockets first)
    [InlineData(true, DevRoleState.None, true, false)]
    [InlineData(false, DevRoleState.None, true, false)]
    public void Dev_transport_is_only_for_a_dev_role_without_a_real_session_in_development(
        bool hasSession, string devRole, bool isDevelopment, bool expected)
        => Assert.Equal(expected, RealtimeConnectionPolicy.UseDevTransport(hasSession, devRole, isDevelopment));

    [Theory]
    [InlineData(true, DevRoleState.None, true, true)]
    [InlineData(true, DevRoleState.None, false, true)] // a real session works in every environment
    [InlineData(true, Roles.Admin, false, true)]
    [InlineData(false, Roles.Citizen, true, true)]
    [InlineData(false, Roles.Citizen, false, false)]   // anonymous in production: never connect, never poll
    [InlineData(false, Roles.Admin, false, false)]
    [InlineData(false, DevRoleState.None, true, false)]
    [InlineData(false, "", true, false)]
    public void Realtime_only_runs_when_the_caller_can_actually_authenticate(
        bool hasSession, string devRole, bool isDevelopment, bool expected)
        => Assert.Equal(expected, RealtimeConnectionPolicy.ShouldConnect(hasSession, devRole, isDevelopment));

    [Fact]
    public void Two_different_signed_in_users_never_share_an_identity_key()
    {
        var first = RealtimeConnectionPolicy.IdentityKey(
            hasSession: true, userId: "11111111-1111-1111-1111-111111111111",
            devRole: DevRoleState.None, isDevelopment: false);
        var second = RealtimeConnectionPolicy.IdentityKey(
            hasSession: true, userId: "22222222-2222-2222-2222-222222222222",
            devRole: DevRoleState.None, isDevelopment: false);

        Assert.NotEqual(first, second);
        Assert.StartsWith("jwt:", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dev_role_switch_changes_the_identity_key()
        => Assert.NotEqual(
            RealtimeConnectionPolicy.IdentityKey(false, null, Roles.Admin, isDevelopment: true),
            RealtimeConnectionPolicy.IdentityKey(false, null, Roles.Citizen, isDevelopment: true));

    [Theory]
    [InlineData(false, DevRoleState.None, true)]
    [InlineData(false, Roles.Admin, false)] // anonymous production visitor: no identity, no traffic
    public void An_unauthenticatable_caller_has_no_identity_key(
        bool hasSession, string devRole, bool isDevelopment)
        => Assert.Null(RealtimeConnectionPolicy.IdentityKey(hasSession, userId: null, devRole, isDevelopment));

    [Fact]
    public void Polling_backs_off_once_the_hub_is_live()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), RealtimeConnectionPolicy.PollInterval(hubConnected: false));
        Assert.Equal(TimeSpan.FromSeconds(60), RealtimeConnectionPolicy.PollInterval(hubConnected: true));
    }

    [Fact]
    public void The_reconnect_schedule_matches_the_blueprint()
        => Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)],
            RealtimeConnectionPolicy.ReconnectDelays);
}
