using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Alerts.Data;
using RapidRelief.Api.Features.Alerts.Domain;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Handlers;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Alerts;

public sealed class AlertsEndpointsTests : IClassFixture<TestingWebAppFactory>
{
    private const string BasePath = "/api/alerts";
    private readonly TestingWebAppFactory _factory;

    public AlertsEndpointsTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var alerts = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await alerts.Alerts.ExecuteDeleteAsync();
        await notifications.Reads.ExecuteDeleteAsync();
        await notifications.Notifications.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task CreateAlert_FlowsIntoF9NotificationStore_WithZeroF9Changes()
    {
        await ResetAsync();
        var response = await Client(Roles.Government).PostAsJsonAsync(BasePath, new
        {
            title = "Cyclone warning",
            body = "Evacuate to high ground immediately.",
            severity = Severity.Catastrophic,
            disasterType = DisasterType.Cyclone,
            targetArea = "Coastal divisions",
            radiusKm = 25.0,
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(6),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var alert = await scope.ServiceProvider.GetRequiredService<AlertsDbContext>().Alerts.AsNoTracking().SingleAsync();
        var notification = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications.AsNoTracking()
            .SingleAsync(x => x.Topic == AlertPublishedNotificationHandler.Topic);

        Assert.Equal("Cyclone warning", alert.Title);
        Assert.Equal(Severity.Catastrophic, alert.Severity);
        Assert.Equal(DisasterType.Cyclone, alert.DisasterType);
        Assert.Equal("Cyclone warning", notification.Summary);
        Assert.Contains(alert.Id.ToString(), notification.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Active_alerts_exclude_expired_and_revoked_records()
    {
        await ResetAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
            db.Alerts.AddRange(
                new Alert { Title = "Current", Body = "Stay alert", Severity = Severity.Severe, TargetArea = "Dhaka", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), CreatedAtUtc = DateTimeOffset.UtcNow },
                new Alert { Title = "Expired", Body = "Old", Severity = Severity.Minor, TargetArea = "Dhaka", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1), CreatedAtUtc = DateTimeOffset.UtcNow },
                new Alert { Title = "Revoked", Body = "Withdrawn", Severity = Severity.Catastrophic, TargetArea = "Dhaka", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), RevokedAtUtc = DateTimeOffset.UtcNow, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var response = await _factory.CreateClient().GetAsync($"{BasePath}/active");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Dictionary<string, List<AlertDto>>>();
        Assert.NotNull(envelope);
        Assert.Single(envelope!["data"]);
        Assert.Equal("Current", envelope["data"][0].Title);
    }

    private sealed record AlertDto(
        Guid Id,
        string Title,
        string Body,
        Severity Severity,
        DisasterType? DisasterType,
        string TargetArea,
        double? RadiusKm,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? RevokedAtUtc);
}
