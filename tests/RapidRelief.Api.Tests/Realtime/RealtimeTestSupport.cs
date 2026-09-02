using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Features.Realtime.Endpoints;
using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>Shared plumbing for the F9 server tests (seeding, hub clients over TestServer).</summary>
internal static class RealtimeTestSupport
{
    public static readonly Guid CitizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];
    public static readonly Guid RescueId = FakeAuthHandler.SeedUserIds[Roles.Rescue];
    public static readonly Guid AdminId = FakeAuthHandler.SeedUserIds[Roles.Admin];
    public static readonly Guid NgoId = FakeAuthHandler.SeedUserIds[Roles.Ngo];

    public static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(2);

    public static HttpClient ClientWithRole(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    public static async Task ResetAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await db.Reads.ExecuteDeleteAsync();
        await db.Notifications.ExecuteDeleteAsync();
    }

    public static async Task<Guid> SeedAsync(
        IServiceProvider services,
        string audience,
        string? role,
        Guid? userId,
        string topic,
        DateTimeOffset createdAtUtc,
        string summary = "seeded")
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var id = Guid.NewGuid();
        db.Notifications.Add(new Notification
        {
            Id = id,
            Audience = audience,
            Role = role,
            UserId = userId,
            Topic = topic,
            Summary = summary,
            PayloadJson = "{}",
            CreatedAtUtc = createdAtUtc,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Generic on purpose: the bus resolves IEventHandler&lt;TEvent&gt; from the STATIC type.</summary>
    public static async Task PublishAsync<TEvent>(IServiceProvider services, TEvent evt) where TEvent : IEvent
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(evt);
    }

    /// <summary>
    /// Long polling is the only transport that can carry the X-Dev-Role header (D-035), and
    /// TestServer has no real socket — HttpMessageHandlerFactory routes it in-process.
    /// </summary>
    public static HubConnection BuildConnection(
        WebApplicationFactory<Program> factory, string devRole, bool automaticReconnect = false)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationsHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[FakeAuthHandler.HeaderName] = devRole;
            });

        if (automaticReconnect)
        {
            builder.WithAutomaticReconnect();
        }

        return builder.Build();
    }

    public static ReceivedNotifications Listen(HubConnection connection)
        => new(connection);
}

/// <summary>Collects pushes so tests can await one or assert that none arrived.</summary>
internal sealed class ReceivedNotifications
{
    private readonly System.Threading.Channels.Channel<NotificationDto> _channel =
        System.Threading.Channels.Channel.CreateUnbounded<NotificationDto>();

    public ReceivedNotifications(HubConnection connection)
        => connection.On<NotificationDto>(NotificationsHub.MethodName, dto => _channel.Writer.TryWrite(dto));

    public async Task<NotificationDto> NextAsync()
    {
        using var cts = new CancellationTokenSource(RealtimeTestSupport.ReceiveTimeout);
        return await _channel.Reader.ReadAsync(cts.Token);
    }

    public async Task AssertNothingArrivesAsync()
    {
        await Task.Delay(RealtimeTestSupport.SilenceWindow);
        Assert.False(_channel.Reader.TryRead(out var unexpected),
            $"Unexpected push received: {unexpected?.Topic}");
    }
}
