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
        _logger.LogInformation("Ping {PingId} created with message \"{Message}\"", evt.PingId, evt.Message);
        return Task.CompletedTask;
    }
}
