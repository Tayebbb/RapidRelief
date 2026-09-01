using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Features.Auth.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ㉟–㊱.</summary>
public sealed class AuthSeederTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public AuthSeederTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact] // ㉟ — idempotency: factory already seeded once at boot
    public async Task Running_seeder_again_changes_nothing_and_does_not_throw()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var usersBefore = await db.Users.CountAsync();
        var rolesBefore = await db.Roles.CountAsync();
        Assert.Equal(4, usersBefore);
        Assert.Equal(4, rolesBefore);

        await AuthSeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);

        Assert.Equal(usersBefore, await db.Users.CountAsync());
        Assert.Equal(rolesBefore, await db.Roles.CountAsync());
    }

    [Fact] // ㊱a — D-017 role GUIDs (authoritative DECISIONS-table values)
    public async Task Seeded_role_ids_match_the_d017_fixed_guids()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var roles = await db.Roles.ToListAsync();

        Assert.Equal(4, roles.Count);
        foreach (var role in Roles.All)
        {
            var row = roles.SingleOrDefault(r => r.Name == role); // exact case — "NGO" (risk 12)
            Assert.NotNull(row);
            Assert.Equal(AuthSeeder.RoleIds[role], row!.Id);
        }
        Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), AuthSeeder.RoleIds[Roles.Citizen]);
        Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), AuthSeeder.RoleIds[Roles.Ngo]);
    }

    [Fact] // ㊱b — demo users carry the FakeAuth GUIDs and can authenticate with Demo!123
    public async Task Seeded_demo_users_match_fake_auth_seed_ids_and_verify_demo_password()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        foreach (var role in Roles.All)
        {
            var email = $"{role.ToLowerInvariant()}1@rr.dev";
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal(FakeAuthHandler.SeedUserIds[role], user!.Id);
            Assert.Equal($"{role} One", user.DisplayName);
            Assert.True(await userManager.CheckPasswordAsync(user, "Demo!123"));
            Assert.Contains(role, await userManager.GetRolesAsync(user));
        }
    }
}
