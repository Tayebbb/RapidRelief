using RapidRelief.Client.Common.Map;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

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

    public static MapPlacemark ToPlacemark(this RescueTeamLiveDto team) =>
        new MapPlacemark(
            team.Id.ToString("N"),
            new GeoPoint(team.Latitude, team.Longitude),
            team.TeamName,
            Detail: $"{team.Speciality} · {team.Status}",
            Status: team.Status,
            IsCritical: string.Equals(team.Status, "OnScene", StringComparison.OrdinalIgnoreCase),
            Weight: 2.0);

    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<RescueTeamLiveDto> teams)
        => teams.Select(ToPlacemark);
}
