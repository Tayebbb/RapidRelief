using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace RapidRelief.Api.Infrastructure.Auth;

/// <summary>
/// MultiAuth policy scheme composition (finding 8): X-Dev-Role header AND FakeAuth-enabled
/// env (Development/Testing) forwards to FakeAuth, everything else to JwtBearer.
/// </summary>
public static class AuthSetup
{
    public const string MultiAuthScheme = "MultiAuth";

    public static IServiceCollection AddRapidReliefAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var fakeAuthEnabled = env.IsDevelopment() || env.IsEnvironment("Testing");

        // Fail-fast outside Dev/Testing: JwtBearer is the only scheme there, and a missing or
        // short key would otherwise fail silently per-request instead of at startup.
        if (!fakeAuthEnabled)
        {
            var signingKey = config["Jwt:SigningKey"];
            if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey is missing or shorter than 32 bytes. Outside Development/Testing a real key is "
                    + "required — provide it via the Jwt__SigningKey environment variable or your secret store "
                    + "(user-secrets, key vault); never commit it to appsettings.json.");
            }
        }

        var authBuilder = services.AddAuthentication(MultiAuthScheme);

        authBuilder.AddPolicyScheme(MultiAuthScheme, MultiAuthScheme, options =>
        {
            options.ForwardDefaultSelector = context =>
                fakeAuthEnabled && context.Request.Headers.ContainsKey(FakeAuthHandler.HeaderName)
                    ? FakeAuthHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme;
        });

        authBuilder.AddJwtBearer(options =>
        {
            var signingKey = config["Jwt:SigningKey"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = config["Jwt:Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // D-013: issuer and validator share one clock — the 5-min default skew only stretches TTLs.
                ClockSkew = TimeSpan.FromMinutes(1),
            };
            if (!string.IsNullOrWhiteSpace(signingKey))
            {
                options.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            }

            options.Events = new JwtBearerEvents
            {
                // SignalR clients pass the bearer token via ?access_token= on hub paths (F9-ready).
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!StringValues.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        if (fakeAuthEnabled)
        {
            // Loud, greppable banner: this line must NEVER appear in production logs.
            Log.Warning("FAKE AUTH ACTIVE (env: {Env}) — X-Dev-Role header authentication is enabled", env.EnvironmentName);
            authBuilder.AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, _ => { });
        }

        services.AddAuthorization(AuthPolicies.Configure);
        return services;
    }
}
