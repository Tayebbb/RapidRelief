namespace RapidRelief.Shared.Contracts.Eventing;

public interface IEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}
