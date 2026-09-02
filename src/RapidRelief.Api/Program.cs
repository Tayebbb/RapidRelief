using System.Net;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Eventing;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Api.Infrastructure.RateLimiting;
using RapidRelief.Api.Infrastructure.Storage;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Services;
using Serilog;

// B6 step 1 — Serilog bootstrap logger, replaced by the config-driven logger below.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    // preserveStaticLogger keeps each host's logger independent so multiple
    // WebApplicationFactory hosts in one test process never re-freeze the bootstrap logger.
    builder.Host.UseSerilog((context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services),
        preserveStaticLogger: true);

    var isTesting = builder.Environment.IsEnvironment("Testing");

    // D-011 — forwarded headers are OPT-IN for reverse-proxy deploys (Proxy:Enabled). Rate
    // limiting partitions per-IP, so proxied deployments MUST configure this or every client
    // shares the proxy's IP partition. KnownNetworks/Proxies are cleared only when proxies
    // are explicitly listed (Proxy:KnownProxies) — never blindly trust any upstream.
    var proxyEnabled = builder.Configuration.GetValue("Proxy:Enabled", false);
    if (proxyEnabled)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            var knownProxies = builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [];
            if (knownProxies.Length > 0)
            {
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var proxy in knownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            }
        });
    }

    // B6 step 2 — ProblemDetails + exception handling (shared framework, no packages).
    builder.Services.AddProblemDetails();

    // B6 step 3 — rate limiter: global per-IP fixed window + named policy skeletons; skipped in Testing.
    if (!isTesting)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            var rateLimiting = builder.Configuration.GetSection("RateLimiting");

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimiting.GetValue("Global:PermitLimit", 100),
                        Window = TimeSpan.FromSeconds(rateLimiting.GetValue("Global:WindowSeconds", 10)),
                        QueueLimit = 0,
                    }));

            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimiting.GetValue("Auth:PermitLimit", 10),
                        Window = TimeSpan.FromSeconds(rateLimiting.GetValue("Auth:WindowSeconds", 60)),
                        QueueLimit = 0,
                    }));

            options.AddPolicy("reports", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimiting.GetValue("Reports:PermitLimit", 30),
                        Window = TimeSpan.FromSeconds(rateLimiting.GetValue("Reports:WindowSeconds", 60)),
                        QueueLimit = 0,
                    }));

            options.AddPolicy("ai", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimiting.GetValue("Ai:PermitLimit", 30),
                        Window = TimeSpan.FromSeconds(rateLimiting.GetValue("Ai:WindowSeconds", 60)),
                        QueueLimit = 0,
                    }));

            // D-054: one POST is one live OpenRouter call, so the assistant gets a much tighter
            // per-user budget than the "ai" read policy it shares a group with.
            options.AddPolicy("assistant", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitions.UserOrIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimiting.GetValue("Assistant:PermitLimit", 12),
                        Window = TimeSpan.FromSeconds(rateLimiting.GetValue("Assistant:WindowSeconds", 300)),
                        QueueLimit = 0,
                    }));

            // Realtime endpoints are all RequireAuthorization, so a caller key always exists:
            // partitioning per user keeps shared-IP clients off each other's budget.
            options.AddPolicy("realtime", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitions.UserOrIp(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimiting.GetValue("Realtime:PermitLimit", 120),
                        Window = TimeSpan.FromSeconds(rateLimiting.GetValue("Realtime:WindowSeconds", 60)),
                        QueueLimit = 0,
                    }));
        });
    }

    // B6 step 4 — FluentValidation validators (EXPLICIT validation only, never auto-MVC).
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // B6 step 5 — MultiAuth policy scheme + JwtBearer + FakeAuth (Dev/Testing) + role policies.
    builder.Services.AddRapidReliefAuth(builder.Configuration, builder.Environment);

    // B6 step 6 — event bus (SCOPED, see B3).
    builder.Services.AddScoped<IEventBus, InProcessEventBus>();

    // B6 step 7 — DatabaseHealth singleton (D-005 degraded-mode flag) + local-disk file storage.
    builder.Services.AddSingleton<DatabaseHealth>();
    builder.Services.AddSingleton<IFileStorage, LocalDiskFileStorage>();

    // B6 step 8 — module discovery + registration (deterministic order).
    var modules = ModuleDiscovery.Discover(typeof(Program).Assembly);
    foreach (var module in modules)
    {
        module.AddModule(builder.Services, builder.Configuration, builder.Environment);
    }

    var app = builder.Build();

    // B6 step 9 — ProblemDetails for exceptions and bare status codes.
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // Post-review item 4a — every response (API, static files, SPA fallback) declares that
    // browsers must not MIME-sniff it. OnStarting + indexer keeps it single-valued.
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(static state =>
        {
            ((HttpContext)state).Response.Headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;
        }, context);
        await next(context);
    });

    // D-011 — must run before anything that consumes scheme/client IP (HTTPS redirect, rate limiter).
    if (proxyEnabled)
    {
        app.UseForwardedHeaders();
    }

    // D-010 — TLS terminates at the app outside Development/Testing: redirect + HSTS.
    if (!app.Environment.IsDevelopment() && !isTesting)
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    // B6 step 10.
    app.UseSerilogRequestLogging();

    // B6 step 11 — hosted Blazor WASM client.
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();

    // B6 step 12.
    app.UseAuthentication();

    // B6 step 13 — AFTER authentication so RateLimitPartitions.UserOrIp sees the real caller;
    // before authorization so unauthenticated floods still consume permits.
    if (!isTesting)
    {
        app.UseRateLimiter();
    }

    app.UseAuthorization();

    // B6 step 14 — each module maps its own endpoints.
    foreach (var module in modules)
    {
        module.MapEndpoints(app);
    }

    // B6 step 15 — SPA fallback to the Blazor client; unknown /api/* routes must stay
    // ProblemDetails 404s and never fall through to the SPA shell.
    app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));
    app.MapFallbackToFile("index.html");

    // B6 step 16 — per-module migrations; warn-and-continue-degraded on failure (D-005). Skipped in
    // Testing (the factory uses SQLite EnsureCreated instead).
    if (!isTesting)
    {
        await MigrationRunner.RunAsync(app.Services, modules);
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "RapidRelief.Api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the entry point to WebApplicationFactory<Program> in integration tests.
public partial class Program
{
}
