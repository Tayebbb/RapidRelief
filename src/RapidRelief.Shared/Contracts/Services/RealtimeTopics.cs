namespace RapidRelief.Shared.Contracts.Services;

/// <summary>
/// The one list of realtime topic names. The server publishes them and the client subscribes to
/// them, so they have to agree letter-for-letter; before this existed each slice kept its own
/// private constant and the client matched them with hand-written string literals.
/// Names follow D-036: lowercase <c>[a-z0-9.]</c>, ≤64 chars, <c>slice.entity.event</c>.
/// </summary>
public static class RealtimeTopics
{
    public const string IncidentReported = "incidents.report.created";
    public const string IncidentStatus = "incidents.report.status";

    /// <summary>AI triage — this is where an incident's severity and priority score change.</summary>
    public const string IncidentAssessed = "ai.incident.assessed";

    public const string RescueMissionAssigned = "rescue.mission.assigned";

    /// <summary>Mission moved along the lifecycle (en route, on scene, completed…).</summary>
    public const string RescueMissionStatus = "rescue.mission.status";

    public const string RescueOperations = "rescue.operations.updated";

    /// <summary>A team went available / dispatched / off duty, or moved.</summary>
    public const string RescueTeamAvailability = "rescue.team.availability";

    public const string ReliefStatus = "relief.request.status";
    public const string AlertPublished = "alerts.published";

    /// <summary>Everything that can change an incident list or an incident marker.</summary>
    public static readonly string[] IncidentFeed =
        [IncidentReported, IncidentStatus, IncidentAssessed];

    /// <summary>Everything that can change a mission, a team or the operations picture.</summary>
    public static readonly string[] RescueFeed =
        [RescueMissionAssigned, RescueMissionStatus, RescueOperations, RescueTeamAvailability];

    /// <summary>The government dashboard reacts to the whole operational surface.</summary>
    public static readonly string[] CommandFeed =
        [.. IncidentFeed, .. RescueFeed, ReliefStatus, AlertPublished];

    /// <summary>What a citizen's own view depends on.</summary>
    public static readonly string[] CitizenFeed =
        [IncidentStatus, IncidentAssessed, RescueMissionStatus, ReliefStatus, AlertPublished];

    /// <summary>
    /// True when <paramref name="topic"/> is one of <paramref name="subscriptions"/>, or sits
    /// under one of them as a dotted prefix — subscribing to <c>rescue</c> gets every rescue
    /// topic, including ones added after the subscriber was written.
    /// </summary>
    public static bool Matches(string? topic, IReadOnlyList<string> subscriptions)
    {
        if (string.IsNullOrEmpty(topic) || subscriptions.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < subscriptions.Count; i++)
        {
            var subscription = subscriptions[i];
            if (string.IsNullOrEmpty(subscription))
            {
                continue;
            }

            if (topic.Equals(subscription, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Prefix match on a segment boundary only: "rescue.mission" must not match
            // "rescue.missionx", and "incidents" must not match "incidentsarchive".
            if (topic.Length > subscription.Length &&
                topic[subscription.Length] == '.' &&
                topic.StartsWith(subscription, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
