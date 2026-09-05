using Npgsql;

namespace RapidRelief.Api.Infrastructure.Persistence;

/// <summary>
/// Brings the app back out of degraded mode without a restart. It only runs while
/// <see cref="DatabaseHealth.PostgresAvailable"/> is false, so a healthy deployment pays nothing
/// for it — and a database that comes back is noticed within one interval instead of never.
///
/// Probes the connection directly rather than through a DbContext: infrastructure must stay
/// feature-agnostic (§4.7), and opening a connection is exactly what was failing.
/// </summary>
public sealed class DatabaseHealthProbe(
    IConfiguration configuration,
    DatabaseHealth health,
    ILogger<DatabaseHealthProbe> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (health.PostgresAvailable != false)
            {
                continue;
            }

            var connectionString = configuration.GetConnectionString("Postgres");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                continue;
            }

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                health.PostgresAvailable = true;
                logger.LogInformation("Database reachable again — leaving degraded mode (D-005)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Still down. Staying degraded is the correct outcome, not an error to escalate.
                logger.LogDebug(ex, "Database still unreachable — remaining in degraded mode");
            }
        }
    }
}
