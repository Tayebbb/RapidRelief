namespace RapidRelief.Api.Infrastructure.Persistence;

/// <summary>
/// D-005 degraded-mode flag. null = not yet determined, true = migrations applied /
/// store reachable, false = degraded (DB-backed endpoints return 503, /health reports it).
/// </summary>
public sealed class DatabaseHealth
{
    public bool? PostgresAvailable { get; set; }
}
