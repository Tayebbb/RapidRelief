namespace RapidRelief.Shared.Contracts.Services;

public interface IRealtimeNotifier
{
    Task NotifyAllAsync(string topic, object payload, CancellationToken ct = default);
    Task NotifyRoleAsync(string role, string topic, object payload, CancellationToken ct = default);
    Task NotifyUserAsync(Guid userId, string topic, object payload, CancellationToken ct = default);
}
