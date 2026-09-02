using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;
using ClientModels = RapidRelief.Client.Common.Realtime;
using ServerWire = RapidRelief.Api.Features.Realtime.Endpoints;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// The client hand-mirrors F9's wire records (D-019 precedent) and hardcodes the hub route and
/// push method name. Nothing in the client build can catch a server-side rename, so these pins
/// are the drift alarm: rename a field or the method and the server suite goes red.
/// </summary>
public sealed class ClientWireContractTests : IClassFixture<TestingWebAppFactory>
{
    private const string HubPath = "/hubs/notifications";
    private const string PushMethod = "notification";
    private const string InboxPath = "/api/realtime/notifications";

    // Relative to the live clock so the pins cannot rot on a future calendar date.
    private static readonly DateTimeOffset Anchor = DateTimeOffset.UtcNow.AddHours(-1);

    private readonly TestingWebAppFactory _factory;

    public ClientWireContractTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public void The_route_constants_are_the_literals_the_client_hardcodes()
    {
        Assert.Equal(HubPath, Api.Features.Realtime.Hubs.NotificationsHub.Path);
        Assert.Equal(PushMethod, Api.Features.Realtime.Hubs.NotificationsHub.MethodName);
        Assert.Equal(InboxPath, ServerWire.NotificationEndpoints.BasePath);
    }

    [Theory]
    [InlineData(typeof(ServerWire.NotificationDto), typeof(ClientModels.NotificationDto))]
    [InlineData(typeof(ServerWire.NotificationPage), typeof(ClientModels.NotificationPage))]
    [InlineData(typeof(ServerWire.MarkedResponse), typeof(ClientModels.MarkedResponse))]
    [InlineData(typeof(ServerWire.UnreadCountResponse), typeof(ClientModels.UnreadCountResponse))]
    public void Every_client_mirror_matches_its_server_record_property_for_property(
        Type serverRecord, Type clientMirror)
    {
        static IEnumerable<string> Shape(Type type) => type
            .GetProperties()
            .Select(p => $"{p.Name}:{Readable(p.PropertyType)}")
            .OrderBy(s => s, StringComparer.Ordinal);

        Assert.Equal(Shape(serverRecord), Shape(clientMirror));
    }

    [Fact]
    public async Task The_inbox_item_json_carries_every_field_the_client_binds()
    {
        await RealtimeTestSupport.ResetAsync(_factory.Services);
        await RealtimeTestSupport.SeedAsync(_factory.Services, NotificationAudience.User, null,
            RealtimeTestSupport.AdminId, "wire.pin.item", Anchor);

        var response = await RealtimeTestSupport.ClientWithRole(_factory, Roles.Admin).GetAsync(InboxPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data");
        Assert.Equal(["items", "nextCursor", "serverTimeUtc"], PropertyNames(data));
        var item = data.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(
            ["audience", "createdAtUtc", "id", "isRead", "payloadJson", "role", "summary", "topic", "userId"],
            PropertyNames(item));
    }

    [Fact]
    public async Task The_count_and_marked_responses_use_the_client_property_names()
    {
        await RealtimeTestSupport.ResetAsync(_factory.Services);
        await RealtimeTestSupport.SeedAsync(_factory.Services, NotificationAudience.All, null, null,
            "wire.pin.count", Anchor);
        var client = RealtimeTestSupport.ClientWithRole(_factory, Roles.Citizen);

        var count = JsonDocument.Parse(await client.GetStringAsync($"{InboxPath}/unread-count"))
            .RootElement.GetProperty("data");
        using var markedResponse = await client.PostAsync($"{InboxPath}/read-all", content: null);
        var marked = JsonDocument.Parse(await markedResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data");

        Assert.Equal(["count"], PropertyNames(count));
        Assert.Equal(["marked"], PropertyNames(marked));
    }

    [Fact]
    public async Task The_hub_pushes_that_same_json_under_the_literal_method_name()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, HubPath), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[FakeAuthHandler.HeaderName] = Roles.Admin;
            })
            .Build();
        var received = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
        connection.On<JsonElement>(PushMethod, push => received.Writer.TryWrite(push));
        await connection.StartAsync();

        await _factory.Services.GetRequiredService<IRealtimeNotifier>()
            .NotifyAllAsync("wire.pin.push", new { Title = "pinned" });

        using var cts = new CancellationTokenSource(RealtimeTestSupport.ReceiveTimeout);
        var payload = await received.Reader.ReadAsync(cts.Token);
        Assert.Equal(
            ["audience", "createdAtUtc", "id", "isRead", "payloadJson", "role", "summary", "topic", "userId"],
            PropertyNames(payload));
        Assert.Equal("wire.pin.push", payload.GetProperty("topic").GetString());
    }

    private static IReadOnlyList<string> PropertyNames(JsonElement element)
        => element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static string Readable(Type type)
        => type.IsGenericType
            ? $"{type.Name}<{string.Join(",", type.GetGenericArguments().Select(Readable))}>"
            : type.Name;
}
