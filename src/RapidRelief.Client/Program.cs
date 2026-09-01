using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RapidRelief.Client;
using RapidRelief.Client.Common.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<DevRoleState>();

// Every API call flows through DevRoleHandler, which stamps X-Dev-Role from DevRoleState
// on relative/same-origin requests only. In WASM, HttpClientHandler delegates to fetch.
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(sp => new HttpClient(
    new DevRoleHandler(sp.GetRequiredService<DevRoleState>(), baseAddress) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = baseAddress,
});

await builder.Build().RunAsync();
