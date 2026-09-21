namespace RapidRelief.Shared.Contracts.ReadModels;

/// <summary>
/// Rescue capacity at a moment in time, so consumers can weigh how long help will realistically
/// take without reaching into the rescue slice's tables.
/// </summary>
public sealed record ResponderAvailabilityDto(
    int TotalTeams,
    int AvailableTeams,
    int DeployedTeams,
    int OpenMissions,
    double? NearestAvailableKm)
{
    public static ResponderAvailabilityDto Unknown { get; } = new(0, 0, 0, 0, null);
}
