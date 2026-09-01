using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Api.Infrastructure.Eventing;

/// <summary>
/// Hand-rolled in-process pub/sub (D-006 — never MediatR). Registered SCOPED so scoped
/// handlers (e.g. future DbContext-using subscribers) resolve from the ambient scope.
/// </summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InProcessEventBus> _logger;

    public InProcessEventBus(IServiceProvider serviceProvider, ILogger<InProcessEventBus> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : IEvent
    {
        // Sequential, per-handler try/catch: a failing subscriber never breaks the publisher
        // or later handlers. Zero handlers = silent success. No Task.Run (disposed-scope bugs).
        foreach (var handler in _serviceProvider.GetServices<IEventHandler<TEvent>>())
        {
            try
            {
                await handler.HandleAsync(evt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler {Handler} failed for {Event} {EventId}",
                    handler.GetType().Name, typeof(TEvent).Name, evt.EventId);
            }
        }
    }
}
