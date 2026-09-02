using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Auth.Services;

/// <summary>
/// Idempotent seeding for Roles (Citizen, Rescuer, Government), Permissions, Role-Permissions,
/// and Dev/Testing Demo Users.
/// </summary>
public static class AuthSeeder
{
    /// <summary>Fixed role GUIDs.</summary>
    public static readonly IReadOnlyDictionary<string, Guid> RoleIds = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        [Roles.Citizen] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        [Roles.Rescuer] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
        [Roles.Government] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
    };

    public const string DemoPassword = "Demo!123";

    /// <summary>Definition of all standard permissions and their page routes.</summary>
    public static readonly IReadOnlyList<PermissionSeedItem> PermissionDefinitions = new List<PermissionSeedItem>
    {
        // Public & Common Pages
        new("Pages.Home.View", "Public Landing Page", "Public", "View public landing page and emergency overview", "/", [Roles.Citizen, Roles.Rescuer, Roles.Government]),
        new("Pages.Auth.Login", "Authentication Access", "Public", "Access sign-in and account registration", "/login", [Roles.Citizen, Roles.Rescuer, Roles.Government]),
        new("Pages.Profile.Manage", "Profile Management", "Common", "Manage personal profile and emergency contacts", "/profile", [Roles.Citizen, Roles.Rescuer, Roles.Government]),
        new("Pages.Notifications.Inbox", "Emergency Notifications", "Common", "View personal and broadcast emergency notifications", "/notifications", [Roles.Citizen, Roles.Rescuer, Roles.Government]),
        new("Pages.Assistant.Chat", "AI Emergency Assistant", "Common", "Converse with the AI emergency response assistant", "/assistant", [Roles.Citizen, Roles.Rescuer, Roles.Government]),
        new("Pages.Sample.View", "Live Grid Telemetry", "Common", "View sample live disaster telemetry map", "/sample", [Roles.Citizen, Roles.Rescuer, Roles.Government]),

        // Citizen Pages & Actions
        new("Pages.Reports.Create", "Create Disaster Report & SOS", "Citizen", "Submit disaster incidents and trigger SOS signals", "/reports/new", [Roles.Citizen, Roles.Government]),
        new("Pages.Reports.MyReports", "Track My Reports", "Citizen", "View timeline and status of filed emergency reports", "/reports/my", [Roles.Citizen, Roles.Government]),
        new("Pages.Shelters.Finder", "Shelter Finder", "Citizen", "Find nearby shelters with live capacity telemetry", "/shelters/finder", [Roles.Citizen, Roles.Rescuer, Roles.Government]),
        new("Pages.Relief.Request", "Request Emergency Relief", "Citizen", "Request food, water, medical, or shelter supplies", "/relief/request", [Roles.Citizen, Roles.Government]),

        // Rescuer Pages & Actions
        new("Pages.Rescuer.Missions", "Rescue Mission Queue", "Rescuer", "View and accept assigned rescue operations", "/rescuer/missions", [Roles.Rescuer, Roles.Government]),
        new("Pages.Rescuer.LiveMap", "Tactical Responder Map", "Rescuer", "Access live responder tactical GIS incident map", "/rescuer/map", [Roles.Rescuer, Roles.Government]),
        new("Pages.Rescuer.TeamStatus", "Team Status & Location", "Rescuer", "Update rescue team readiness and GPS coordinates", "/rescuer/team", [Roles.Rescuer, Roles.Government]),

        // Government (Admin / Full System Access) Pages & Actions
        new("Pages.Government.CommandCenter", "Central Command Center", "Government", "Monitor all real-time disaster metrics and operations", "/admin/command", [Roles.Government]),
        new("Pages.Government.Incidents", "Incident Triage & Verification", "Government", "Verify, triage, and score priority of disaster reports", "/admin/incidents", [Roles.Government]),
        new("Pages.Government.Dispatch", "Mission Dispatch", "Government", "Assign rescue missions and units to incidents", "/admin/dispatch", [Roles.Government]),
        new("Pages.Government.Shelters", "Shelter Management", "Government", "Create shelters, manage capacity and supply inventories", "/admin/shelters", [Roles.Government]),
        new("Pages.Government.Resources", "Relief Stock & Inventory", "Government", "Manage warehouse stocks and approve relief requests", "/admin/resources", [Roles.Government]),
        new("Pages.Government.Broadcast", "Emergency Broadcast Alerts", "Government", "Dispatch mass public emergency notifications and SMS/push alerts", "/admin/broadcast", [Roles.Government]),
        new("Pages.Government.Analytics", "Disaster Analytics & Heatmaps", "Government", "Analyze response KPIs, historical trends, and risk heatmaps", "/admin/analytics", [Roles.Government]),
        new("Pages.Government.Users", "User & Role Administration", "Government", "Manage user accounts, assign roles, and handle lockouts", "/admin/users", [Roles.Government]),
        new("Pages.Government.Permissions", "Role Permission Matrix", "Government", "Configure page access permissions across roles", "/admin/permissions", [Roles.Government]),
        new("Pages.Government.Audit", "System Audit Trail", "Government", "Inspect security logs, event trails, and administrative actions", "/admin/audit", [Roles.Government]),
    };

    public record PermissionSeedItem(string Code, string Name, string Category, string Description, string PageRoute, IReadOnlyList<string> AllowedRoles);

    public static async Task SeedAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var roleManager = scopedServices.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var db = scopedServices.GetRequiredService<AuthDbContext>();
        var env = scopedServices.GetRequiredService<IHostEnvironment>();

        // 1. Seed Roles
        foreach (var role in Roles.All)
        {
            ct.ThrowIfCancellationRequested();
            if (!await roleManager.RoleExistsAsync(role))
            {
                ThrowOnFailure(await roleManager.CreateAsync(
                    new IdentityRole<Guid>(role) { Id = RoleIds[role] }), $"role '{role}'");
            }
        }

        // 2. Seed Permissions Catalog
        var existingPermissions = await db.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var def in PermissionDefinitions)
        {
            ct.ThrowIfCancellationRequested();
            if (!existingPermissions.TryGetValue(def.Code, out var perm))
            {
                perm = new AppPermission
                {
                    Id = Guid.NewGuid(),
                    Code = def.Code,
                    Name = def.Name,
                    Category = def.Category,
                    Description = def.Description,
                    PageRoute = def.PageRoute,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                };
                db.Permissions.Add(perm);
                existingPermissions[def.Code] = perm;
            }
        }
        await db.SaveChangesAsync(ct);

        // 3. Seed Role-Permissions Join Matrix
        var existingRolePermissions = await db.RolePermissions.ToListAsync(ct);
        foreach (var def in PermissionDefinitions)
        {
            ct.ThrowIfCancellationRequested();
            var perm = existingPermissions[def.Code];
            foreach (var roleName in def.AllowedRoles)
            {
                var roleId = RoleIds[roleName];
                if (!existingRolePermissions.Any(rp => rp.RoleId == roleId && rp.PermissionId == perm.Id))
                {
                    db.RolePermissions.Add(new AppRolePermission
                    {
                        RoleId = roleId,
                        PermissionId = perm.Id,
                        AssignedAtUtc = DateTimeOffset.UtcNow,
                    });
                }
            }
        }
        await db.SaveChangesAsync(ct);

        // 4. Seed Demo Users in Development/Testing
        if (!(env.IsDevelopment() || env.IsEnvironment("Testing")))
        {
            return;
        }

        var userManager = scopedServices.GetRequiredService<UserManager<AppUser>>();
        
        var seedUsers = new List<(string Email, string Role, Guid Id, string DisplayName)>
        {
            ("citizen1@rr.dev", Roles.Citizen, Guid.Parse("11111111-1111-1111-1111-111111111111"), "Citizen One"),
            ("rescuer1@rr.dev", Roles.Rescuer, Guid.Parse("22222222-2222-2222-2222-222222222222"), "Rescuer One"),
            ("government1@rr.dev", Roles.Government, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Government One"),
            // Legacy aliases for backward compatibility
            ("admin1@rr.dev", Roles.Government, Guid.Parse("33333333-3333-3333-3333-333333333334"), "Admin One"),
            ("rescue1@rr.dev", Roles.Rescuer, Guid.Parse("22222222-2222-2222-2222-222222222224"), "Rescue One"),
            ("ngo1@rr.dev", Roles.Government, Guid.Parse("44444444-4444-4444-4444-444444444444"), "NGO One"),
        };

        foreach (var (email, role, id, displayName) in seedUsers)
        {
            ct.ThrowIfCancellationRequested();
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new AppUser
            {
                Id = id,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
            };
            ThrowOnFailure(await userManager.CreateAsync(user, DemoPassword), $"user '{email}'");
            ThrowOnFailure(await userManager.AddToRoleAsync(user, role), $"user '{email}' role");
        }
    }

    private static void ThrowOnFailure(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"AuthSeeder failed creating {what}: " + string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
        }
    }
}
