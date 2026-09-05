using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
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
        IAuditTrail audit,
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

        if (!await userAdmin.SetLockedAsync(id, request.Locked, ct))
        {
            return UserNotFound();
        }

        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            request.Locked ? "User.Lock" : "User.Unlock", "User", id.ToString(),
            request.Locked ? "Account locked out of the platform" : "Account restored",
            request.Locked ? "Locked" : "Unlocked"), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> SetRolesAsync(
        Guid id,
        SetRolesRequest request,
        IValidator<SetRolesRequest> validator,
        ClaimsPrincipal principal,
        IUserAdminService userAdmin,
        IAuditTrail audit,
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
        if (!await userAdmin.SetRolesAsync(id, request.Roles!, ct))
        {
            return UserNotFound();
        }

        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            "User.Roles", "User", id.ToString(),
            $"Roles set to {string.Join(", ", request.Roles!)}", "Updated"), ct);

        return Results.NoContent();
    }

    private static bool IsSelf(ClaimsPrincipal principal, Guid id) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId) && callerId == id;

    private static IResult SelfActionProblem(string title) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: title);

    private static async Task<IResult> DeleteUserAsync(
        Guid id,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        IAuditTrail audit,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return AuthEndpoints.DatabaseUnavailable();
        }

        if (IsSelf(principal, id))
        {
            return SelfActionProblem("You cannot delete your own account");
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return UserNotFound();
        }

        // Deleting the last Government account would strand the platform with nobody able to
        // verify an incident or promote a replacement.
        if (await userManager.IsInRoleAsync(user, Roles.Government)
            && (await userManager.GetUsersInRoleAsync(Roles.Government)).Count <= 1)
        {
            return SelfActionProblem("You cannot delete the last Government account");
        }

        var email = user.Email ?? user.UserName ?? id.ToString();
        var result = await userManager.DeleteAsync(user);
        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            "User.Delete", "User", id.ToString(), $"Deleted account {email}",
            result.Succeeded ? "Deleted" : "Failed"), ct);

        return result.Succeeded ? Results.NoContent() : Results.BadRequest(result.Errors);
    }

    /// <summary>
    /// Demo-reset escape hatch. Refused outside Development/Testing — an authenticated admin
    /// mistake here is unrecoverable, so it must not exist on a live deployment.
    /// </summary>
    private static async Task<IResult> DeleteAllUsersAsync(
        UserManager<AppUser> userManager,
        AuthDbContext db,
        IAuditTrail audit,
        IHostEnvironment env,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Not available in this environment",
                detail: "Bulk user deletion is a development-only reset.");
        }

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

        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            "User.DeleteAll", "User", "*", $"Bulk deleted {count} of {users.Count} accounts", "Deleted"), ct);

        return Results.Ok(new { deletedCount = count });
    }

    private static IResult UserNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found");
}
