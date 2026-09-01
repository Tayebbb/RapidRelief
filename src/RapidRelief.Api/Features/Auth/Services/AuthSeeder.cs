using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Auth.Services;

/// <summary>
/// Blueprint B8 — idempotent seeding, invoked from BOTH AuthModule.MigrateAsync and
/// TestingWebAppFactory (MigrationRunner never runs in Testing, risk 3). Roles are seeded in
/// every environment with fixed GUIDs (D-017); the 4 demo users only where FakeAuth is
/// enabled (Development/Testing) with GUIDs matching FakeAuthHandler.SeedUserIds.
/// </summary>
public static class AuthSeeder
{
    /// <summary>Fixed role GUIDs (D-017, authoritative DECISIONS-table values).</summary>
    public static readonly IReadOnlyDictionary<string, Guid> RoleIds = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        [Roles.Citizen] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        [Roles.Rescue] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
        [Roles.Admin] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
        [Roles.Ngo] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"),
    };

    /// <summary>§5 demo password — Development/Testing only, never a production credential.</summary>
    public const string DemoPassword = "Demo!123";

    public static async Task SeedAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var roleManager = scopedServices.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var env = scopedServices.GetRequiredService<IHostEnvironment>();

        foreach (var role in Roles.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!await roleManager.RoleExistsAsync(role))
            {
                ThrowOnFailure(await roleManager.CreateAsync(
                    new IdentityRole<Guid>(role) { Id = RoleIds[role] }), $"role '{role}'");
            }
        }

        if (!(env.IsDevelopment() || env.IsEnvironment("Testing")))
        {
            return; // no demo users outside the FakeAuth-enabled environments
        }

        var userManager = scopedServices.GetRequiredService<UserManager<AppUser>>();
        foreach (var role in Roles.All)
        {
            ct.ThrowIfCancellationRequested();
            var email = $"{role.ToLowerInvariant()}1@rr.dev";
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new AppUser
            {
                Id = FakeAuthHandler.SeedUserIds[role], // FakeAuth and real Identity are the same people
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = $"{role} One",
            };
            ThrowOnFailure(await userManager.CreateAsync(user, DemoPassword), $"user '{email}'");
            ThrowOnFailure(await userManager.AddToRoleAsync(user, role), $"user '{email}' role");
        }
    }

    private static void ThrowOnFailure(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            // Loud, not silent — a broken seed would otherwise surface as mysterious 401s.
            throw new InvalidOperationException(
                $"AuthSeeder failed creating {what}: " + string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
        }
    }
}
