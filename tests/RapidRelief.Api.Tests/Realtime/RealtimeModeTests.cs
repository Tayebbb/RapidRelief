using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Realtime;
using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// D-032 mode matrix. PollingOnly is the DoD's "degrades to polling": no hub route, but the
/// notifier still persists so the inbox endpoints keep serving. Off pins the permanent no-op.
/// </summary>
public sealed class RealtimeModeTests
{
    private const string Base = "/api/realtime/notifications";
    private const string Negotiate = NotificationsHub.Path + "/negotiate?negotiateVersion=1";

    private static WebApplicationFactory<Program> FactoryFor(TestingWebAppFactory root, string mode)
        => root.WithWebHostBuilder(builder => builder.UseSetting("Realtime:Mode", mode));

    private static HttpClient Client(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    private static async Task<int> ItemCountAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("items").GetArrayLength();
    }

    [Fact]
    public async Task Polling_only_drops_the_hub_route_but_keeps_persisting()
    {
        using var root = new TestingWebAppFactory();
        using var factory = FactoryFor(root, "PollingOnly");
        var client = Client(factory, Roles.Citizen);

        var negotiate = await client.PostAsync(Negotiate, content: null);
        await factory.Services.GetRequiredService<IRealtimeNotifier>()
            .NotifyAllAsync("polling.only", new { Title = "still arrives" });
        var inbox = await client.GetAsync(Base);

        Assert.Equal(HttpStatusCode.NotFound, negotiate.StatusCode);
        Assert.IsType<SignalRRealtimeNotifier>(factory.Services.GetRequiredService<IRealtimeNotifier>());
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.Equal(1, await ItemCountAsync(inbox));
    }

    [Fact]
    public async Task Off_uses_the_permanent_no_op_notifier_and_writes_nothing()
    {
        using var root = new TestingWebAppFactory();
        using var factory = FactoryFor(root, "Off");
        var client = Client(factory, Roles.Citizen);

        var negotiate = await client.PostAsync(Negotiate, content: null);
        await factory.Services.GetRequiredService<IRealtimeNotifier>()
            .NotifyAllAsync("off.mode", new { Title = "dropped" });
        var inbox = await client.GetAsync(Base);

        Assert.Equal(HttpStatusCode.NotFound, negotiate.StatusCode);
        Assert.IsType<NoOpRealtimeNotifier>(factory.Services.GetRequiredService<IRealtimeNotifier>());
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.Equal(0, await ItemCountAsync(inbox));
    }

    [Fact]
    public async Task Hub_mode_is_the_default_and_maps_the_hub()
    {
        using var root = new TestingWebAppFactory();
        var client = Client(root, Roles.Citizen);

        var negotiate = await client.PostAsync(Negotiate, content: null);

        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
        Assert.IsType<SignalRRealtimeNotifier>(root.Services.GetRequiredService<IRealtimeNotifier>());
    }
}
