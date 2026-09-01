using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Features.Sample.Handlers;

/// <summary>Demo subscriber proving the publish→handle loop (D-008).</summary>
public sealed class PingCreatedLoggingHandler : IEventHandler<PingCreated>
{
    private readonly ILogger<PingCreatedLoggingHandler> _logger;

    public PingCreatedLoggingHandler(ILogger<PingCreatedLoggingHandler> logger) => _logger = logger;

    public Task HandleAsync(PingCreated evt, CancellationToken ct = default)
    {
        // Log hygiene: user text is data — log IDs/lengths, never the message itself.
        _logger.LogInformation("Ping {PingId} created ({Length} chars)", evt.PingId, evt.Message.Length);
        return Task.CompletedTask;
    }
}
