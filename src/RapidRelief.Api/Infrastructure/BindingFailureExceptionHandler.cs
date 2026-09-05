using Microsoft.AspNetCore.Diagnostics;

namespace RapidRelief.Api.Infrastructure;

/// <summary>
/// Minimal-API binding failures (missing/unparsable route or query values) throw
/// <see cref="BadHttpRequestException"/>, which the exception handler would otherwise report as a
/// 500. This keeps them the 400 ProblemDetails that docs/api-conventions.md promises.
/// </summary>
public sealed class BindingFailureExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = badRequest.StatusCode,
                Title = "Bad request",
                Detail = "One or more route or query string values are missing or malformed.",
            },
        });
    }
}
