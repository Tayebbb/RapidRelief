using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using RapidRelief.Client;
using RapidRelief.Client.Common.Auth;
using RapidRelief.Client.Common.Geo;
using RapidRelief.Client.Common.Realtime;
using RapidRelief.Client.Features.Assistant;
using RapidRelief.Client.Features.Alerts;
using RapidRelief.Client.Features.Auth;
using RapidRelief.Client.Features.Shelters;

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

// F16 assistant rides the MAIN scoped client so Bearer / X-Dev-Role behave as everywhere else.
builder.Services.AddScoped<IAssistantApi>(sp => new AssistantApi(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped<IAlertsApi>(sp => new AlertsApi(sp.GetRequiredService<HttpClient>()));

// Realtime (F9). The notification singletons outlive the scoped main client, so they get their
// own instance of the SAME handler chain — Bearer and X-Dev-Role behave identically.
builder.Services.AddSingleton<INotificationsApi>(sp => new NotificationsApi(ApiClient(sp)));
builder.Services.AddSingleton<NotificationState>();
builder.Services.AddSingleton(sp => new NotificationHubClient(
    sp.GetRequiredService<JwtAuthStateProvider>(),
    sp.GetRequiredService<AuthApi>(),
    sp.GetRequiredService<DevRoleState>(),
    sp.GetRequiredService<NotificationState>(),
    baseAddress,
    isDevelopment,
    sp.GetRequiredService<ILogger<NotificationHubClient>>()));

// F3 Client
builder.Services.AddScoped(sp => new SheltersClient(sp.GetRequiredService<HttpClient>()));

// Foundation geolocation (browser prompt only fires on user action — see js/geolocation.js).
builder.Services.AddScoped<GeolocationService>();

var host = builder.Build();

// Silent session restore on boot (F5) — must never block or crash an offline PWA start.
try
{
    await host.Services.GetRequiredService<AuthApi>().TryRefreshAsync();
}
catch
{
    // boot stays anonymous
}

// Connect the hub / start polling if that restore (or a dev role) gives us an identity.
try
{
    await host.Services.GetRequiredService<NotificationHubClient>().SyncAsync();
}
catch
{
    // realtime is best-effort; the inbox endpoints keep working without it
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
