using RapidRelief.Api.Features.Stubs;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Stubs;

public sealed class FakeUserAdminServiceTests
{
    [Fact]
    public async Task GetUsersAsync_returns_the_three_seeded_identities_with_fake_auth_guids()
    {
        var service = new FakeUserAdminService();

        var result = await service.GetUsersAsync();

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        foreach (var role in Roles.All)
        {
            var user = Assert.Single(result.Items, u => u.Roles.Contains(role));
            Assert.Equal(FakeAuthHandler.SeedUserIds[role], user.Id);
            Assert.Equal($"{role.ToLowerInvariant()}1@rr.dev", user.Email);
            Assert.False(user.IsLocked);
        }
    }

    [Fact]
    public async Task SetLockedAsync_returns_false_for_unknown_id()
    {
        var service = new FakeUserAdminService();

        var result = await service.SetLockedAsync(Guid.Parse("99999999-9999-9999-9999-999999999999"), locked: true);

        Assert.False(result);
    }

    [Fact]
    public async Task SetLockedAsync_locks_a_known_user_in_memory()
    {
        var service = new FakeUserAdminService();
        var adminId = FakeAuthHandler.SeedUserIds[Roles.Government];

        var result = await service.SetLockedAsync(adminId, locked: true);

        Assert.True(result);
        var users = await service.GetUsersAsync();
        Assert.True(users.Items.Single(u => u.Id == adminId).IsLocked);
    }

    [Fact]
    public async Task SetRolesAsync_returns_false_for_unknown_id_and_updates_known_users()
    {
        var service = new FakeUserAdminService();
        var citizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];

        var unknown = await service.SetRolesAsync(Guid.Parse("88888888-8888-8888-8888-888888888888"), [Roles.Government]);
        var known = await service.SetRolesAsync(citizenId, [Roles.Citizen, Roles.Rescuer]);

        Assert.False(unknown);
        Assert.True(known);
        var users = await service.GetUsersAsync();
        var citizen = users.Items.Single(u => u.Id == citizenId);
        Assert.Equal(new[] { Roles.Citizen, Roles.Rescuer }, citizen.Roles);
    }

    [Fact]
    public async Task GetUsersAsync_paging_math_is_consistent()
    {
        var service = new FakeUserAdminService();

        var page = await service.GetUsersAsync(page: 2, pageSize: 2);

        Assert.Equal(3, page.TotalCount);
        Assert.Single(page.Items);
    }
}
