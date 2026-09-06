using System.Text.Json;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Common.Realtime;

public sealed record NotificationParsedDetails(
    string Category,
    string CategoryIcon,
    string CategoryBadgeClass,
    string? TargetUrl,
    string ActionLabel,
    string? BadgeText,
    string? BadgeClass,
    string? IncidentId,
    string? MissionId,
    string? AlertId,
    string? Status,
    string? Severity,
    double? PriorityScore,
    bool IsSos,
    double? Latitude,
    double? Longitude,
    string? DisasterType,
    string? DetailText);

public static class NotificationActionHelper
{
    public static NotificationParsedDetails Parse(NotificationDto notification, string? userRole = null)
    {
        var topic = notification.Topic ?? string.Empty;
        var summary = notification.Summary ?? string.Empty;
        var role = userRole ?? Roles.Citizen;

        string? incidentId = null;
        string? missionId = null;
        string? alertId = null;
        string? status = null;
        string? severity = null;
        double? priorityScore = null;
        bool isSos = false;
        double? latitude = null;
        double? longitude = null;
        string? disasterType = null;
        string? detailText = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(notification.PayloadJson))
            {
                using var doc = JsonDocument.Parse(notification.PayloadJson);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("incidentId", out var incEl))
                    {
                        incidentId = incEl.GetString();
                    }
                    else if (root.TryGetProperty("IncidentId", out incEl))
                    {
                        incidentId = incEl.GetString();
                    }

                    if (root.TryGetProperty("missionId", out var misEl))
                    {
                        missionId = misEl.GetString();
                    }
                    else if (root.TryGetProperty("MissionId", out misEl))
                    {
                        missionId = misEl.GetString();
                    }

                    if (root.TryGetProperty("alertId", out var altEl))
                    {
                        alertId = altEl.GetString();
                    }
                    else if (root.TryGetProperty("AlertId", out altEl))
                    {
                        alertId = altEl.GetString();
                    }

                    if (root.TryGetProperty("status", out var statEl))
                    {
                        status = statEl.GetString();
                    }
                    else if (root.TryGetProperty("Status", out statEl))
                    {
                        status = statEl.GetString();
                    }

                    if (root.TryGetProperty("severity", out var sevEl))
                    {
                        severity = sevEl.GetString();
                    }
                    else if (root.TryGetProperty("estimatedSeverity", out sevEl) || root.TryGetProperty("Severity", out sevEl))
                    {
                        severity = sevEl.ValueKind == JsonValueKind.Number ? sevEl.GetInt32().ToString() : sevEl.GetString();
                    }

                    if (root.TryGetProperty("priorityScore", out var prioEl) && prioEl.TryGetDouble(out var pVal))
                    {
                        priorityScore = pVal;
                    }

                    if (root.TryGetProperty("isSos", out var sosEl))
                    {
                        isSos = sosEl.GetBoolean();
                    }

                    if (root.TryGetProperty("latitude", out var latEl) && latEl.TryGetDouble(out var latVal))
                    {
                        latitude = latVal;
                    }

                    if (root.TryGetProperty("longitude", out var lonEl) && lonEl.TryGetDouble(out var lonVal))
                    {
                        longitude = lonVal;
                    }

                    if (root.TryGetProperty("type", out var typeEl))
                    {
                        disasterType = typeEl.GetString();
                    }
                    else if (root.TryGetProperty("Type", out typeEl))
                    {
                        disasterType = typeEl.GetString();
                    }

                    if (root.TryGetProperty("body", out var bodyEl))
                    {
                        detailText = bodyEl.GetString();
                    }
                    else if (root.TryGetProperty("Body", out bodyEl))
                    {
                        detailText = bodyEl.GetString();
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully on parsing issues
        }

        // Check summary for hints if not in payload
        if (!isSos && (summary.Contains("SOS", StringComparison.OrdinalIgnoreCase) || topic.Contains("sos", StringComparison.OrdinalIgnoreCase)))
        {
            isSos = true;
        }

        // Categorization & Target Route resolution
        string category;
        string icon;
        string badgeClass;
        string? targetUrl;
        string actionLabel;
        string? badgeText = null;
        string? itemBadgeClass = null;

        if (isSos)
        {
            category = "SOS Alert";
            icon = "alert-triangle";
            badgeClass = "tag-sos";
            badgeText = "CRITICAL SOS";
            itemBadgeClass = "badge-danger";
            targetUrl = ResolveIncidentUrl(role, incidentId);
            actionLabel = role == Roles.Government ? "Review SOS in Command" : (role == Roles.Rescuer ? "Respond to SOS" : "Track My SOS");
        }
        else if (topic.StartsWith("ai.", StringComparison.OrdinalIgnoreCase))
        {
            category = "AI Triage";
            icon = "sparkles";
            badgeClass = "tag-ai";
            badgeText = priorityScore.HasValue ? $"Priority {priorityScore.Value:0.#}" : "AI Assessed";
            itemBadgeClass = "badge-purple";
            targetUrl = ResolveIncidentUrl(role, incidentId);
            actionLabel = role == Roles.Government ? "Inspect Triage in Command" : (role == Roles.Rescuer ? "View Tactical Priority" : "View Assessment");
        }
        else if (topic.StartsWith("rescue.", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(missionId))
        {
            category = "Rescue Mission";
            icon = "activity";
            badgeClass = "tag-mission";
            badgeText = !string.IsNullOrEmpty(status) ? $"Status: {status}" : "Mission Update";
            itemBadgeClass = status == "Completed" ? "badge-success" : "badge-info";
            targetUrl = role == Roles.Rescuer ? "r" : (role == Roles.Government ? "g" : "reports/my");
            actionLabel = role == Roles.Rescuer ? "Open Mission HUD" : (role == Roles.Government ? "Command View" : "Track Mission");
        }
        else if (topic.StartsWith("incidents.", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(incidentId))
        {
            category = "Incident Report";
            icon = "alert-circle";
            badgeClass = "tag-incident";
            badgeText = !string.IsNullOrEmpty(disasterType) ? disasterType : "New Report";
            itemBadgeClass = "badge-warning";
            targetUrl = ResolveIncidentUrl(role, incidentId);
            actionLabel = role == Roles.Government ? "Manage Incident" : (role == Roles.Rescuer ? "View Incident" : "Track Report");
        }
        else if (topic.StartsWith("alerts.", StringComparison.OrdinalIgnoreCase) || topic.StartsWith("alert.", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(alertId))
        {
            category = "Public Alert";
            icon = "bell";
            badgeClass = "tag-alert";
            badgeText = "Broadcast";
            itemBadgeClass = "badge-danger";
            targetUrl = role == Roles.Government ? "alerts/compose" : "notifications";
            actionLabel = role == Roles.Government ? "Manage Broadcasts" : "View Alert Details";
        }
        else if (topic.StartsWith("shelters.", StringComparison.OrdinalIgnoreCase))
        {
            category = "Shelter";
            icon = "home";
            badgeClass = "tag-shelter";
            badgeText = "Shelter";
            itemBadgeClass = "badge-success";
            targetUrl = role == Roles.Government ? "admin/shelters" : "shelters/finder";
            actionLabel = role == Roles.Government ? "Manage Shelters" : "Find Nearest Shelter";
        }
        else if (topic.StartsWith("relief.", StringComparison.OrdinalIgnoreCase))
        {
            category = "Relief";
            icon = "shield-plus";
            badgeClass = "tag-relief";
            badgeText = "Relief";
            itemBadgeClass = "badge-primary";
            targetUrl = "relief/request";
            actionLabel = "View Relief Operations";
        }
        else
        {
            category = "General";
            icon = "info";
            badgeClass = "tag-general";
            targetUrl = "notifications";
            actionLabel = "View in Inbox";
        }

        return new NotificationParsedDetails(
            category,
            icon,
            badgeClass,
            targetUrl,
            actionLabel,
            badgeText,
            itemBadgeClass,
            incidentId,
            missionId,
            alertId,
            status,
            severity,
            priorityScore,
            isSos,
            latitude,
            longitude,
            disasterType,
            detailText);
    }

    private static string ResolveIncidentUrl(string role, string? incidentId)
    {
        return role switch
        {
            Roles.Government => "g",
            Roles.Rescuer => "r",
            _ => "reports/my"
        };
    }
}
