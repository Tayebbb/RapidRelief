using RapidRelief.Api.Infrastructure.Modules;

namespace RapidRelief.Api.Infrastructure.Persistence;

/// <summary>
/// B6 step 16 — per-module startup migrations with retry; on total failure the app keeps
/// serving in degraded mode (D-005). NEVER crashes the host.
/// </summary>
public static class MigrationRunner
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    public static async Task RunAsync(
        IServiceProvider services,
        IReadOnlyList<IFeatureModule> modules,
        TimeSpan? retryDelay = null,
        CancellationToken ct = default)
    {
        var delay = retryDelay ?? DefaultRetryDelay;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MigrationRunner));
        var health = services.GetRequiredService<DatabaseHealth>();
        var allSucceeded = true;

        foreach (var module in modules)
        {
            var succeeded = false;
            for (var attempt = 1; attempt <= MaxAttempts && !succeeded; attempt++)
            {
                try
                {
                    // Fresh scope per attempt: a failed connection never poisons the retry.
                    using var scope = services.CreateScope();
                    await module.MigrateAsync(scope.ServiceProvider, ct);
                    succeeded = true;
                }
                catch (Exception ex)
                {
                    if (attempt < MaxAttempts)
                    {
                        logger.LogWarning(ex,
                            "Migration attempt {Attempt}/{MaxAttempts} failed for module {Module}; retrying in {DelaySeconds}s",
                            attempt, MaxAttempts, module.Name, delay.TotalSeconds);
                        try
                        {
                            await Task.Delay(delay, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            // Never-crash contract (D-005): swallow the cancellation, mark degraded, stop.
                            logger.LogWarning(
                                "Migration run cancelled while waiting to retry module {Module} — stopping; app continues in DEGRADED mode (D-005)",
                                module.Name);
                            health.PostgresAvailable = false;
                            return;
                        }
                    }
                    else
                    {
                        logger.LogError(ex,
                            "Migration FAILED for module {Module} after {MaxAttempts} attempts — continuing in DEGRADED mode (D-005): DB-backed endpoints return 503, /health reports dbConnected=false",
                            module.Name, MaxAttempts);
                        allSucceeded = false;
                    }
                }
            }
        }

        health.PostgresAvailable = allSucceeded;
        if (allSucceeded)
        {
            logger.LogInformation("All module migrations applied — database available");
        }
    }
}
