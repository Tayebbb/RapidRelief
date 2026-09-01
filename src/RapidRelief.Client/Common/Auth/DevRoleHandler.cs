namespace RapidRelief.Client.Common.Auth;

/// <summary>
/// Injects X-Dev-Role from <see cref="DevRoleState"/> into every API call. Harmless outside
/// Development/Testing: the server only registers FakeAuth there, so the header is ignored
/// everywhere else (JwtBearer takes over).
/// </summary>
public sealed class DevRoleHandler : DelegatingHandler
{
    private const string HeaderName = "X-Dev-Role";
    private readonly DevRoleState _state;
    private readonly Uri? _baseAddress;

    public DevRoleHandler(DevRoleState state, Uri? baseAddress = null)
    {
        _state = state;
        _baseAddress = baseAddress;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(HeaderName);
        if (_state.CurrentRole is not DevRoleState.None &&
            HttpOrigin.IsRelativeOrSameOrigin(request.RequestUri, _baseAddress))
        {
            request.Headers.Add(HeaderName, _state.CurrentRole);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
