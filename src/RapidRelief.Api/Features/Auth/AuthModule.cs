using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Features.Auth.Endpoints;
using RapidRelief.Api.Features.Auth.Services;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Auth;

/// <summary>F1 vertical slice (blueprint B9). NEVER AddIdentity — it would hijack MultiAuth (risk 1).</summary>
public sealed class AuthModule : IFeatureModule
{
    public string Name => "Auth";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing")) // factory injects SQLite itself (B6 step 8 precedent)
        {
            services.AddDbContext<AuthDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(AuthDbContext.MigrationsHistoryTableName)));
        }

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true; // default is FALSE; login is by email (risk 9)
                // password/lockout stay at Identity defaults (≥6 w/ classes; 5 attempts / 5 min)
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddSignInManager(); // CheckPasswordSignInAsync w/o cookies; no AddDefaultTokenProviders (R1)

        services.AddHttpContextAccessor(); // SignInManager dependency
        services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = config.GetValue("Auth:PasswordHasherIterations", 210_000)); // D-018
        services.TryAddSingleton(TimeProvider.System); // D-009 precedent; AiModule also registers it
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IUserAdminService, IdentityUserAdminService>(); // displaces the fake via stub-yield
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        UserAdminEndpoints.Map(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<AuthDbContext>().Database.MigrateAsync(ct);
        await AuthSeeder.SeedAsync(scopedServices, ct);
    }
}
