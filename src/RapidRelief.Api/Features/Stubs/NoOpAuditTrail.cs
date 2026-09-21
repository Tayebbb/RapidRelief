using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Stubs;

/// <summary>
/// Stub-yield fallback (B5): keeps every feature that records audit lines working when the
/// Audit module is absent. Drops the record — it never fails the caller's action.
/// </summary>
public sealed class NoOpAuditTrail : IAuditTrail
{
    public Task RecordAsync(AuditRecord record, CancellationToken ct = default) => Task.CompletedTask;
}
