using System.Security.Claims;
using RapidRelief.Api.Features.Audit.Data;
using RapidRelief.Api.Features.Audit.Domain;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Audit.Services;

/// <summary>
/// Writes the append-only trail. Never throws: a failed audit write is logged and swallowed so
/// it can never roll back the administrative action the operator just completed (D-098).
/// </summary>
public sealed class AuditTrail : IAuditTrail
{
    private readonly AuditDbContext _db;
    private readonly DatabaseHealth _health;
    private readonly IHttpContextAccessor _http;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuditTrail> _logger;

    public AuditTrail(
        AuditDbContext db,
        DatabaseHealth health,
        IHttpContextAccessor http,
        TimeProvider clock,
        ILogger<AuditTrail> logger)
    {
        _db = db;
        _health = health;
        _http = http;
        _clock = clock;
        _logger = logger;
    }

    public async Task RecordAsync(AuditRecord record, CancellationToken ct = default)
    {
        if (_health.PostgresAvailable != true)
        {
            return;
        }

        try
        {
            var user = _http.HttpContext?.User;
            var actorId = record.ActorId ?? ResolveUserId(user);

            _db.Entries.Add(new AuditEntry
            {
                ActorId = actorId,
                ActorName = Fallback(record.ActorName, ResolveName(user), actorId is null ? "system" : "unknown"),
                ActorRole = Fallback(record.ActorRole, ResolveRole(user), "system"),
                Action = Trim(record.Action, 80),
                EntityType = Trim(record.EntityType, 60),
                EntityId = Trim(record.EntityId, 80),
                Summary = Trim(record.Summary, 500),
                Result = Trim(string.IsNullOrWhiteSpace(record.Result) ? "Succeeded" : record.Result, 80),
                Source = user?.Identity?.IsAuthenticated == true ? AuditSource.Operator : AuditSource.Event,
                OccurredAtUtc = _clock.GetUtcNow(),
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit write failed for {Action} on {EntityType} {EntityId}",
                record.Action, record.EntityType, record.EntityId);
        }
    }

    private static Guid? ResolveUserId(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static string ResolveName(ClaimsPrincipal? user) =>
        user?.FindFirstValue("unique_name")
        ?? user?.FindFirstValue(ClaimTypes.Name)
        ?? user?.FindFirstValue(ClaimTypes.Email)
        ?? string.Empty;

    private static string ResolveRole(ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    private static string Fallback(params string[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;

    private static string Trim(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
