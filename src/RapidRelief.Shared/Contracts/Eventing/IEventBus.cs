namespace RapidRelief.Shared.Contracts.Eventing;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : IEvent;
}
