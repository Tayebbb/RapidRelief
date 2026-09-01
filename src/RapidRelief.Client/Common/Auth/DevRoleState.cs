namespace RapidRelief.Client.Common.Auth;

/// <summary>
/// Holds the dev role sent as X-Dev-Role on every API call. Defaults to None: outside
/// Development the header is ignored by the server, so a non-None default would only make
/// anonymous visitors act as if they were signed in.
/// </summary>
public sealed class DevRoleState
{
    public const string None = "None";

    public string CurrentRole { get; private set; } = None;

    public event Action? Changed;

    public void SetRole(string role)
    {
        if (CurrentRole == role)
        {
            return;
        }
        CurrentRole = role;
        Changed?.Invoke();
    }
}
