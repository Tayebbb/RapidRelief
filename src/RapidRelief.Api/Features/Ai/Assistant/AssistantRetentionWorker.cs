using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Infrastructure.Persistence;

namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>
/// D-048 retention sweep: deletes assistant messages older than Ai:Assistant:RetentionDays
/// (7 — chat text can describe the user's situation, so it is shorter than F9's 30) in
/// batches, plus one sweep shortly after startup. Skipped while degraded.
/// </summary>
public sealed class AssistantRetentionWorker : BackgroundService
{
    public const int BatchSize = 500;

    /// <summary>Lets migrations and the degraded-mode probe settle before the first sweep.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AssistantOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AssistantRetentionWorker> _logger;

    public AssistantRetentionWorker(
        IServiceScopeFactory scopeFactory,
        AssistantOptions options,
        TimeProvider timeProvider,
        ILogger<AssistantRetentionWorker> logger)
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
            _logger.LogDebug("Database degraded — assistant retention sweep skipped");
            return 0;
        }

        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var cutoff = _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays);
        var deleted = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.AssistantMessages
                .Where(m => m.CreatedAtUtc < cutoff)
                .OrderBy(m => m.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            db.AssistantMessages.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            deleted += batch.Count;

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Assistant retention sweep deleted {Count} rows older than {Cutoff:o}",
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
            _logger.LogError(ex, "Assistant retention sweep failed");
        }
    }
}
