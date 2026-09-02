using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Endpoints;
using RapidRelief.Api.Features.Realtime.Handlers;
using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Api.Features.Realtime.Pipeline;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Realtime;

/// <summary>
/// Tayeb's F9 lane. D-032 tri-state: Hub = SignalR + hub route + persist&amp;push ·
/// PollingOnly = no SignalR, notifier still persists so the endpoints serve polling ·
/// Off = the permanent NoOpRealtimeNotifier (§4.5). Endpoints are mapped in all three modes.
/// </summary>
public sealed class RealtimeModule : IFeatureModule
{
    private RealtimeMode _mode = RealtimeMode.Hub;

    public string Name => "Realtime";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var options = RealtimeOptions.Read(config);
        // Program reuses the same module instance for MapEndpoints — cache the mode here.
        _mode = options.Mode;

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HubConnectionRegistry>();

        // The no-op stays registered concretely forever (§4.5) and is the binding in Mode=Off.
        services.AddSingleton<NoOpRealtimeNotifier>();
        if (options.Mode == RealtimeMode.Off)
        {
            services.AddSingleton<IRealtimeNotifier>(sp => sp.GetRequiredService<NoOpRealtimeNotifier>());
        }
        else
        {
            services.AddSingleton<IRealtimeNotifier>(sp => new SignalRRealtimeNotifier(
                sp.GetRequiredService<IServiceScopeFactory>(),
                // Null in PollingOnly: no SignalR registration, so persistence carries the feature.
                sp.GetService<IHubContext<NotificationsHub>>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<SignalRRealtimeNotifier>>()));
        }

        if (options.Mode == RealtimeMode.Hub)
        {
            services.AddSignalR().AddHubOptions<NotificationsHub>(hub =>
            {
                // Push-only hub: clients never invoke anything, so keep the inbound budget tiny.
                hub.MaximumReceiveMessageSize = 2 * 1024;
                hub.EnableDetailedErrors = false;
            });
        }

        services.AddScoped<IEventHandler<IncidentAssessed>, IncidentAssessedNotificationHandler>();
        services.AddScoped<IEventHandler<AlertPublished>, AlertPublishedNotificationHandler>();
        services.AddScoped<IEventHandler<AuthEvent>, AuthEventDisconnectHandler>();
        services.AddHostedService<NotificationRetentionWorker>();

        // Npgsql ONLY outside Testing — the test factory injects its own SQLite options.
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<NotificationsDbContext>(dbOptions =>
                dbOptions.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(NotificationsDbContext.MigrationsHistoryTableName)));
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        if (_mode == RealtimeMode.Hub)
        {
            // The path MUST start with /hubs or the access_token query hook in AuthSetup no-ops.
            endpoints.MapHub<NotificationsHub>(NotificationsHub.Path, hub =>
            {
                hub.CloseOnAuthenticationExpiration = true;
                hub.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
            });
        }
        else
        {
            // Without this, the SPA fallback would answer a negotiate with an HTML page; a
            // clean 404 tells the client the hub is off and to stay on polling.
            endpoints.MapFallback("/hubs/{**path}", () => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Realtime hub disabled",
                detail: "This deployment runs without SignalR; poll /api/realtime/notifications instead."));
        }

        NotificationEndpoints.Map(endpoints);
    }

    public Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
        => scopedServices.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(ct);
}
