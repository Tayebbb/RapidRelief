using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using RapidRelief.Client;
using RapidRelief.Client.Common.Ai;
using RapidRelief.Client.Common.Auth;
using RapidRelief.Client.Common.Geo;
using RapidRelief.Client.Common.Map;
using RapidRelief.Client.Common.Offline;
using RapidRelief.Client.Common.Realtime;
using RapidRelief.Client.Features.Assistant;
using RapidRelief.Client.Features.Alerts;
using RapidRelief.Client.Features.Auth;
using RapidRelief.Client.Features.Command;
using RapidRelief.Client.Features.Relief;
using RapidRelief.Client.Features.Reports;
using RapidRelief.Client.Features.Rescue;
using RapidRelief.Client.Features.Shelters;
using RapidRelief.Client.Features.CommandCenter;
using RapidRelief.Client.Features.Registry;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
var isDevelopment = builder.HostEnvironment.IsDevelopment();

builder.Services.AddSingleton<DevRoleState>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<JwtAuthStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthStateProvider>());

// AuthApi owns a handler-free client: the rr_refresh cookie is its credential (the browser
// attaches it same-origin), and refreshing outside the main chain prevents recursion (risk 10).
builder.Services.AddSingleton(sp => new AuthApi(
    new HttpClient { BaseAddress = baseAddress },
    sp.GetRequiredService<JwtAuthStateProvider>()));

// Main client chain: DevRoleHandler (outer, stamps X-Dev-Role) → AuthMessageHandler (inner,
// attaches Bearer and strips X-Dev-Role while signed in — real login wins) → fetch.
builder.Services.AddScoped(sp => ApiClient(sp));

// Assistant rides the MAIN scoped client so Bearer / X-Dev-Role behave as everywhere else.
builder.Services.AddScoped<IAssistantApi>(sp => new AssistantApi(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped<IAlertsApi>(sp => new AlertsApi(sp.GetRequiredService<HttpClient>()));

// Realtime: the notification singletons outlive the scoped main client, so they get their
// own instance of the SAME handler chain — Bearer and X-Dev-Role behave identically.
builder.Services.AddSingleton<INotificationsApi>(sp => new NotificationsApi(ApiClient(sp)));
builder.Services.AddSingleton<NotificationState>();

// Pages subscribe to topics instead of holding a Timer: while the hub is up there is no polling.
builder.Services.AddSingleton(sp => new LiveUpdateService(
    sp.GetRequiredService<NotificationState>(),
    sp.GetRequiredService<ILogger<LiveUpdateService>>()));

builder.Services.AddSingleton(sp => new NotificationHubClient(
    sp.GetRequiredService<JwtAuthStateProvider>(),
    sp.GetRequiredService<AuthApi>(),
    sp.GetRequiredService<DevRoleState>(),
    sp.GetRequiredService<NotificationState>(),
    baseAddress,
    isDevelopment,
    sp.GetRequiredService<ILogger<NotificationHubClient>>()));

// Shelters client
builder.Services.AddScoped(sp => new SheltersClient(sp.GetRequiredService<HttpClient>()));

// Command center client
builder.Services.AddScoped(sp => new CommandCenterClient(sp.GetRequiredService<HttpClient>()));

// Incident ingestion + rescue operations ride the main Bearer / X-Dev-Role chain.
builder.Services.AddScoped(sp => new IncidentsClient(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped(sp => new RescueClient(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped(sp => new ReliefClient(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped(sp => new RegistryClient(sp.GetRequiredService<HttpClient>()));

// Command centre: aggregates the ops metrics, audit trail, inventory and admin surfaces.
builder.Services.AddScoped(sp => new CommandClient(sp.GetRequiredService<HttpClient>()));

// Decision support: structured insight, explanation and the duplicate review queue.
builder.Services.AddScoped(sp => new AiClient(sp.GetRequiredService<HttpClient>()));

// Store-and-forward outbox: a report typed offline is persisted before any network attempt.
builder.Services.AddScoped(sp => new OutboxService(
    sp.GetRequiredService<IJSRuntime>(),
    sp.GetRequiredService<IncidentsClient>()));

// Foundation geolocation (browser prompt only fires on user action — see js/geolocation.js).
builder.Services.AddScoped<GeolocationService>();

// The one map service: tile settings from the server (never a key in client source) and the
// factory for the per-page MapView that owns layers, filters and distances.
builder.Services.AddScoped(sp => new MapConfigService(sp.GetRequiredService<HttpClient>()));

var host = builder.Build();

// D-117: these three boot steps deliberately never throw past this point (see each comment),
// but they used to swallow the exception with no trace at all — a systemic failure (e.g. a
// cookie/CORS break after a deploy) was invisible except as a vague "I keep getting logged out"
// ticket. Log at Warning so the browser console/telemetry sink still sees it; still never blocks
// or crashes boot.
var bootLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Boot");

// Silent session restore on boot — must never block or crash an offline PWA start.
try
{
    await host.Services.GetRequiredService<AuthApi>().TryRefreshAsync();
}
catch (Exception ex)
{
    bootLogger.LogWarning(ex, "Session restore failed on boot — continuing anonymous");
}

// Connect the hub / start polling if that restore (or a dev role) gives us an identity.
try
{
    await host.Services.GetRequiredService<NotificationHubClient>().SyncAsync();
}
catch (Exception ex)
{
    bootLogger.LogWarning(ex, "Realtime hub sync failed on boot — falling back to polling");
}

// Deliver anything the citizen filed while offline, then keep listening for reconnects.
try
{
    await host.Services.GetRequiredService<OutboxService>().InitializeAsync();
}
catch (Exception ex)
{
    bootLogger.LogWarning(ex, "Offline outbox init failed on boot — offline reports may be stuck queued");
}

await host.RunAsync();

HttpClient ApiClient(IServiceProvider sp) => new(
    new DevRoleHandler(sp.GetRequiredService<DevRoleState>(), baseAddress)
    {
        InnerHandler = new AuthMessageHandler(
            sp.GetRequiredService<JwtAuthStateProvider>(),
            sp.GetRequiredService<AuthApi>(),
            baseAddress)
        {
            InnerHandler = new HttpClientHandler(),
        },
    })
{
    BaseAddress = baseAddress,
};
