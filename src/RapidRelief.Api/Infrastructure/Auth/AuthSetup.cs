using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

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
            authBuilder.AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, _ => { });
        }

        services.AddAuthorization(AuthPolicies.Configure);
        return services;
    }
}
