using System.Net.Http.Headers;
using RapidRelief.Client.Features.Auth;

namespace RapidRelief.Client.Common.Auth;

/// <summary>
/// Attaches the in-memory Bearer to same-origin requests, proactively refreshing when the access
/// token is about to expire (blueprint B10.3). While a real session exists it strips X-Dev-Role —
/// real login always wins over the dev picker (R4). No session ⇒ pass-through, so the FakeAuth
/// flow is preserved untouched.
/// </summary>
public sealed class AuthMessageHandler : DelegatingHandler
{
    private const string DevRoleHeaderName = "X-Dev-Role";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly JwtAuthStateProvider _authState;
    private readonly AuthApi _authApi;
    private readonly Uri? _baseAddress;

    public AuthMessageHandler(JwtAuthStateProvider authState, AuthApi authApi, Uri? baseAddress = null)
    {
        _authState = authState;
        _authApi = authApi;
        _baseAddress = baseAddress;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The Bearer must never leak to third-party origins (e.g. tile servers).
        if (!_authState.HasSession || !HttpOrigin.IsRelativeOrSameOrigin(request.RequestUri, _baseAddress))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (_authState.ExpiresAtUtc - DateTimeOffset.UtcNow < RefreshMargin)
        {
            // Single-flight refresh via AuthApi's handler-free client — never recurses into this
            // chain (risk 10). On failure the session is cleared and the request proceeds anonymously.
            await _authApi.TryRefreshAsync();
        }

        if (_authState.AccessToken is { } accessToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Remove(DevRoleHeaderName);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
