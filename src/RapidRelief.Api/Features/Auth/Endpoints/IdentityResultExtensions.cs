using Microsoft.AspNetCore.Identity;

namespace RapidRelief.Api.Features.Auth.Endpoints;

/// <summary>IdentityResult failures → 400 ValidationProblem keyed by error code (MapIdentityApi shape).</summary>
public static class IdentityResultExtensions
{
    public static IResult ToValidationProblem(this IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);
        return Results.ValidationProblem(errors);
    }
}
