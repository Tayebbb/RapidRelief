namespace RapidRelief.Shared.Contracts.Eventing;

public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent evt, CancellationToken ct = default);
}
