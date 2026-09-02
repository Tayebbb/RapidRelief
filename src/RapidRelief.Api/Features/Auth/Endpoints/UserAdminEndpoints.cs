using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Auth.Endpoints;

/// <summary>Blueprint B5 endpoints 9–11 (RequireAdmin) — thin adapters over the frozen IUserAdminService.</summary>
public static class UserAdminEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.AddEndpointFilter(AuthEndpoints.CacheControlNoStoreFilter);

        group.MapGet("/users", GetUsersAsync)
            .RequireAuthorization(AuthPolicies.RequireAdmin);
        group.MapPost("/users/{id:guid}/lock", SetLockAsync)
            .RequireAuthorization(AuthPolicies.RequireAdmin);
        group.MapPut("/users/{id:guid}/roles", SetRolesAsync)
            .RequireAuthorization(AuthPolicies.RequireAdmin);
        group.MapDelete("/users/{id:guid}", DeleteUserAsync)
            .RequireAuthorization(AuthPolicies.RequireAdmin);
        group.MapDelete("/users/all", DeleteAllUsersAsync)
            .RequireAuthorization(AuthPolicies.RequireAdmin);
    }

    private static async Task<IResult> GetUsersAsync(
        IUserAdminService userAdmin,
        DatabaseHealth databaseHealth,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return AuthEndpoints.DatabaseUnavailable();
        }

        var result = await userAdmin.GetUsersAsync(page, pageSize, ct); // service clamps (B7)
        return Results.Ok(new ApiEnvelope<PagedResult<UserSummaryDto>>(result));
    }

    private static async Task<IResult> SetLockAsync(
        Guid id,
        SetLockRequest request,
        ClaimsPrincipal principal,
        IUserAdminService userAdmin,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return AuthEndpoints.DatabaseUnavailable();
        }

        if (IsSelf(principal, id))
        {
            return SelfActionProblem("Cannot lock your own account.");
        }

        return await userAdmin.SetLockedAsync(id, request.Locked, ct)
            ? Results.NoContent()
            : UserNotFound();
    }

    private static async Task<IResult> SetRolesAsync(
        Guid id,
        SetRolesRequest request,
        IValidator<SetRolesRequest> validator,
        ClaimsPrincipal principal,
        IUserAdminService userAdmin,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return AuthEndpoints.DatabaseUnavailable();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        if (IsSelf(principal, id))
        {
            return SelfActionProblem("Cannot change your own roles.");
        }

        // Validator already excluded bad roles, so false unambiguously means unknown id (B7).
        return await userAdmin.SetRolesAsync(id, request.Roles!, ct)
            ? Results.NoContent()
            : UserNotFound();
    }

    private static bool IsSelf(ClaimsPrincipal principal, Guid id) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId) && callerId == id;

    private static IResult SelfActionProblem(string title) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: title);

    private static async Task<IResult> DeleteUserAsync(
        Guid id,
        UserManager<AppUser> userManager,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return AuthEndpoints.DatabaseUnavailable();
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return UserNotFound();
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded ? Results.NoContent() : Results.BadRequest(result.Errors);
    }

    private static async Task<IResult> DeleteAllUsersAsync(
        UserManager<AppUser> userManager,
        AuthDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return AuthEndpoints.DatabaseUnavailable();
        }

        var users = await db.Users.ToListAsync(ct);
        var count = 0;
        foreach (var u in users)
        {
            var res = await userManager.DeleteAsync(u);
            if (res.Succeeded) count++;
        }

        return Results.Ok(new { deletedCount = count });
    }

    private static IResult UserNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found");
}
