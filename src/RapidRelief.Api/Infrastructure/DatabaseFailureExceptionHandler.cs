using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using RapidRelief.Api.Infrastructure.Persistence;

namespace RapidRelief.Api.Infrastructure;

/// <summary>
/// D-005 degraded mode is only honest if it can be entered at runtime. <see cref="DatabaseHealth"/>
/// is decided once at startup by the migration runner, so a database that dies mid-session used to
/// surface as a raw 500 from every endpoint — the guards all still believed the DB was up.
///
/// This handler turns any database fault into the same 503 the endpoints return deliberately, and
/// flips the health flag so the very next request short-circuits instead of waiting on a dead
/// connection. <see cref="DatabaseHealthProbe"/> is what lets it recover.
/// </summary>
public sealed class DatabaseFailureExceptionHandler(
    IProblemDetailsService problemDetailsService,
    DatabaseHealth health,
    ILogger<DatabaseFailureExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!IsDatabaseFailure(exception))
        {
            return false;
        }

        if (health.PostgresAvailable != false)
        {
            health.PostgresAvailable = false;
            logger.LogError(exception,
                "Database call failed — entering DEGRADED mode (D-005). DB-backed endpoints return 503 until a probe reconnects.");
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Database unavailable",
                // Never echo the provider's message: it carries host names and connection details.
                Detail = "The app is running in degraded mode (D-005): this data is temporarily unavailable. "
                    + "Emergency reports filed while degraded stay on your device and send automatically.",
            },
        });
    }

    /// <summary>
    /// Npgsql reports a missing or unusable connection string as a plain
    /// <see cref="InvalidOperationException"/>, so type alone is not enough to classify it.
    /// </summary>
    internal static bool IsDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException or TimeoutException)
            {
                return true;
            }

            if (current is InvalidOperationException
                && current.Message.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
