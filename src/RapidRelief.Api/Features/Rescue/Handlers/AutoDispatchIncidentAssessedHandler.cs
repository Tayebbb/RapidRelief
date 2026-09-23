using RapidRelief.Api.Features.Rescue.Services;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Features.Rescue.Handlers;

/// <summary>
/// Event handler triggered when an incident assessment completes.
/// Evaluates the priority score and triggers the Government AI Auto-Dispatch Engine
/// for high-urgency and SOS calls.
/// </summary>
public sealed class AutoDispatchIncidentAssessedHandler : IEventHandler<IncidentAssessed>
{
    private readonly IAutoDispatchService _autoDispatch;
    private readonly ILogger<AutoDispatchIncidentAssessedHandler> _logger;

    public AutoDispatchIncidentAssessedHandler(
        IAutoDispatchService autoDispatch,
        ILogger<AutoDispatchIncidentAssessedHandler> logger)
    {
        _autoDispatch = autoDispatch;
        _logger = logger;
    }

    public async Task HandleAsync(IncidentAssessed evt, CancellationToken ct = default)
    {
        try
        {
            var outcome = await _autoDispatch.EvaluateAndDispatchAsync(
                evt.IncidentId,
                evt.PriorityScore,
                evt.EstimatedSeverity,
                ct);

            if (outcome.Dispatched)
            {
                _logger.LogInformation("AI Auto-Dispatch completed for Incident {IncidentId}: {Reason}",
                    evt.IncidentId, outcome.Reason);
            }
            else
            {
                _logger.LogDebug("Incident {IncidentId} not auto-dispatched: {Reason}",
                    evt.IncidentId, outcome.Reason);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AI Auto-Dispatch for incident {IncidentId}", evt.IncidentId);
        }
    }
}
