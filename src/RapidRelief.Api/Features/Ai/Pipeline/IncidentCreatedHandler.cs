using System.Threading.Channels;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Ai.Pipeline;

/// <summary>
/// D-021 ingress: maps IncidentCreated to an AiAnalysisRequest and enqueues it. Never blocks
/// the publishing request — the channel drops (and logs) when full.
/// </summary>
public sealed class IncidentCreatedHandler : IEventHandler<IncidentCreated>
{
    private readonly Channel<AiWorkItem> _channel;
    private readonly ILogger<IncidentCreatedHandler> _logger;

    public IncidentCreatedHandler(Channel<AiWorkItem> channel, ILogger<IncidentCreatedHandler> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public Task HandleAsync(IncidentCreated evt, CancellationToken ct = default)
    {
        var request = new AiAnalysisRequest(evt.IncidentId, evt.Type, evt.Description,
            evt.Location, evt.IsSos, evt.OccurredAtUtc, evt.PhotoPaths,
            evt.ReportedSeverity, evt.AffectedPeopleCount);

        // DropWrite channel: a full queue drops the item (logged by the channel's drop
        // callback); TryWrite only returns false once the writer is completed (shutdown).
        if (!_channel.Writer.TryWrite(new AiWorkItem(request)))
        {
            _logger.LogError("AI pipeline channel closed — analysis work for incident {IncidentId} was not enqueued",
                evt.IncidentId);
        }
        return Task.CompletedTask;
    }
}
