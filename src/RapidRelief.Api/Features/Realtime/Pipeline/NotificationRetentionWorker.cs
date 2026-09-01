using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Infrastructure.Persistence;

namespace RapidRelief.Api.Features.Realtime.Pipeline;

/// <summary>
/// D-034 retention sweep: deletes notifications older than Realtime:RetentionDays in batches
/// of 500 every Realtime:RetentionSweepHours, plus one sweep shortly after startup so a
/// short-lived process still bounds the table. Read rows cascade. Skipped while degraded.
/// </summary>
public sealed class NotificationRetentionWorker : BackgroundService
{
    public const int BatchSize = 500;

    /// <summary>Lets migrations and the degraded-mode probe settle before the first sweep.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RealtimeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationRetentionWorker> _logger;

    public NotificationRetentionWorker(
        IServiceScopeFactory scopeFactory,
        RealtimeOptions options,
        TimeProvider timeProvider,
        ILogger<NotificationRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Deletes expired rows and returns how many were removed.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<DatabaseHealth>().PostgresAvailable != true)
        {
            _logger.LogDebug("Database degraded — notification retention sweep skipped");
            return 0;
        }

        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var cutoff = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
        var deleted = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.Notifications
                .Where(n => n.CreatedAtUtc < cutoff)
                .OrderBy(n => n.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            // Read rows follow via the ON DELETE CASCADE constraint in the Initial migration.
            db.Notifications.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            deleted += batch.Count;

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Notification retention sweep deleted {Count} rows older than {Cutoff:o}",
                deleted, cutoff);
        }

        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.RetentionSweepHours));
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await SweepSafelyAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SweepSafelyAsync(CancellationToken ct)
    {
        try
        {
            await SweepAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed sweep is a bounded leak, never a reason to kill the worker.
            _logger.LogError(ex, "Notification retention sweep failed");
        }
    }
}
