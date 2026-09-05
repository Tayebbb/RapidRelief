using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Common.Ai;

/// <summary>Hand-mirrored wire records (D-045) for the decision-support surface.</summary>
public sealed record AiPriorityFactorDto(string Code, string Label, double Points, string Evidence);

public sealed record AiInsightDto(
    Guid IncidentId,
    DisasterType PredictedType,
    Severity EstimatedSeverity,
    double Confidence,
    string Urgency,
    int? EstimatedPeopleAffected,
    bool MedicalUrgency,
    IReadOnlyList<string> DamageIndicators,
    string Summary,
    string Reasoning,
    double PriorityScore,
    string PriorityBand,
    IReadOnlyList<AiPriorityFactorDto> PriorityFactors,
    string Provider,
    string? ModelName,
    Guid? PossibleDuplicateOfId,
    double? DuplicateConfidence,
    string? DuplicateReason,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>Shown verbatim wherever an insight is rendered — never omitted.</summary>
    public const string Disclaimer = "AI-generated · decision support only. Verify against the report before acting.";
}

public sealed record DuplicateFlagDto(
    Guid IncidentId,
    Guid PossibleDuplicateOfId,
    double Confidence,
    string Reason,
    string? Decision,
    DateTimeOffset FlaggedAtUtc,
    DateTimeOffset? ReviewedAtUtc);

/// <summary>
/// Reads the AI decision-support surface. Every call fails soft — an incident page must still
/// render when the analyser has not run or the assessment endpoint is unreachable.
/// </summary>
public sealed class AiClient(HttpClient http)
{
    public async Task<AiInsightDto?> GetInsightAsync(Guid incidentId, CancellationToken ct = default)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<AiInsightDto>>($"api/ai/insights/{incidentId}", ct);
            return envelope?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DuplicateFlagDto>> GetDuplicatesAsync(bool pendingOnly = true, CancellationToken ct = default)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<List<DuplicateFlagDto>>>(
                $"api/ai/duplicates?pendingOnly={(pendingOnly ? "true" : "false")}", ct);
            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    public Task<string?> ConfirmDuplicateAsync(Guid incidentId, string? note, CancellationToken ct = default)
        => DecideAsync($"api/ai/duplicates/{incidentId}/confirm", note, ct);

    public Task<string?> DismissDuplicateAsync(Guid incidentId, string? note, CancellationToken ct = default)
        => DecideAsync($"api/ai/duplicates/{incidentId}/dismiss", note, ct);

    private async Task<string?> DecideAsync(string url, string? note, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(url, new { note }, ct);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => "Only the command centre can review duplicate flags.",
                System.Net.HttpStatusCode.NotFound => "That flag no longer exists.",
                System.Net.HttpStatusCode.Conflict => "Someone already reviewed this flag.",
                _ => "The decision could not be recorded.",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server.";
        }
    }
}
