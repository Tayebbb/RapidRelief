using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Shared.Contracts.Services;

/// <summary>
/// Append-only trail of administrative actions. Implementations must never throw at the call
/// site — an unrecorded audit line may not fail the action the operator just performed.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditRecord record, CancellationToken ct = default);
}
