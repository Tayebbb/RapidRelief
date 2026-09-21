namespace RapidRelief.Shared.Contracts.Enums;

public static class Roles
{
    public const string Citizen = "Citizen";
    public const string Rescuer = "Rescuer";
    public const string Government = "Government";

    // Backward-compatible aliases for early feature stubs
    public const string Rescue = "Rescuer";
    public const string Admin = "Government";
    public const string Ngo = "Government";

    public static readonly IReadOnlyList<string> All = new[] { Citizen, Rescuer, Government };
}
