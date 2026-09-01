using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RapidRelief.Client;
using RapidRelief.Client.Common.Auth;
using RapidRelief.Client.Features.Auth;
using RapidRelief.Client.Features.Shelters;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);

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
builder.Services.AddScoped(sp => new HttpClient(
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
});

// F3 Client
builder.Services.AddScoped(sp => new SheltersClient(sp.GetRequiredService<HttpClient>()));

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

await host.RunAsync();
