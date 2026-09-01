using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Common.Auth;

/// <summary>Holds the dev role sent as X-Dev-Role on every API call. Default Admin for demo flow.</summary>
public sealed class DevRoleState
{
    public const string None = "None";

    public string CurrentRole { get; private set; } = Roles.Admin;

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
