using RapidRelief.Client.Common.Map;

namespace RapidRelief.Client.Features.Rescue;

/// <summary>Teams onto the shared map. Teams without a reported position are simply not plotted.</summary>
public static class RescueMapAdapters
{
    public static MapPlacemark? ToPlacemark(this RescueTeamDto team)
        => team.CurrentLocation is null
            ? null
            : new MapPlacemark(
                team.Id.ToString("N"),
                team.CurrentLocation,
                team.TeamName,
                Detail: team.ActiveMissionCount > 0
                    ? $"{team.Status} · {team.ActiveMissionCount} active"
                    : team.Status,
                Status: team.Status,
                // An unreachable or fully-committed team is what blocks the next dispatch.
                IsCritical: string.Equals(team.Status, "OffDuty", StringComparison.OrdinalIgnoreCase),
                Weight: Math.Max(1, team.ActiveMissionCount));

    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<RescueTeamDto> teams)
        => teams.Select(ToPlacemark).OfType<MapPlacemark>();
}
