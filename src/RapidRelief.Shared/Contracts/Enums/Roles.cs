namespace RapidRelief.Shared.Contracts.Enums;

public static class Roles
{
    public const string Citizen = "Citizen";
    public const string Rescue = "Rescue";
    public const string Admin = "Admin";
    public const string Ngo = "NGO";

    public static readonly IReadOnlyList<string> All = new[] { Citizen, Rescue, Admin, Ngo };
}
