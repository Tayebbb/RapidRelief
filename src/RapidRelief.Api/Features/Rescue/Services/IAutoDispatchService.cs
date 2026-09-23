using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Rescue.Services;

/// <summary>
/// Result of an auto-dispatch evaluation.
/// </summary>
public sealed record AutoDispatchOutcome(
    bool Dispatched,
    Guid? MissionId,
    Guid? TeamId,
    string? TeamName,
    double? SuitabilityScore,
    string Reason);

/// <summary>
/// Government-side AI Auto-Dispatch contract: evaluates newly assessed incidents
/// and automatically assigns the best available rescue unit based on severity,
/// disaster specialization, distance, and team availability.
/// </summary>
public interface IAutoDispatchService
{
    Task<AutoDispatchOutcome> EvaluateAndDispatchAsync(
        Guid incidentId,
        double priorityScore,
        Severity severity,
        CancellationToken ct = default);
}
