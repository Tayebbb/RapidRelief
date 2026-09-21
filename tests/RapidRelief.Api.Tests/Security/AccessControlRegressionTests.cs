using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Auth.Endpoints;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Security;

/// <summary>
/// Regression cover for the access-control holes found in the final production audit. Each test
/// names the attack it prevents — these are the checks that must never quietly regress.
/// </summary>
public sealed class AccessControlRegressionTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public AccessControlRegressionTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient ClientAs(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    [Fact]
    public async Task A_rescuer_paging_the_incident_feed_does_not_receive_reporter_phone_numbers()
    {
        var response = await ClientAs(Roles.Rescuer).GetAsync("/api/incidents?page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item =>
            Assert.Equal(JsonValueKind.Null, item.GetProperty("contactPhone").ValueKind));
    }

    [Fact]
    public async Task The_command_centre_still_sees_contact_details_it_needs_to_call_back()
    {
        var response = await ClientAs(Roles.Government).GetAsync("/api/incidents?page=1&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.Contains(items, item => item.GetProperty("contactPhone").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task A_rescuer_cannot_dispatch_a_team_they_do_not_belong_to()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RescueDbContext>();
        var now = DateTimeOffset.UtcNow;
        var foreignTeam = new RescueTeam
        {
            Id = Guid.NewGuid(),
            TeamName = "Someone else's unit",
            Specialization = "General",
            TeamLeadUserId = Guid.NewGuid(),
            Status = TeamStatus.Available,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Teams.Add(foreignTeam);
        await db.SaveChangesAsync();

        var response = await ClientAs(Roles.Rescuer).PostAsJsonAsync("/api/rescue/missions", new
        {
            incidentId = Guid.NewGuid(),
            teamId = foreignTeam.Id,
            missionTitle = "Hijack attempt",
            priority = "Critical",
        });

        // Refused before the incident is even resolved — the team is not the caller's to move.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);

        using var verify = _factory.Services.CreateScope();
        var check = verify.ServiceProvider.GetRequiredService<RescueDbContext>();
        var after = await check.Teams.FindAsync(foreignTeam.Id);
        Assert.Equal(TeamStatus.Available, after!.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example.com/steal")]
    [InlineData("//evil.example.com/steal")]
    [InlineData("http://localhost:5179.evil.example.com/auth/callback")]
    [InlineData("javascript:alert(1)")]
    public void An_off_origin_oauth_callback_is_replaced_with_our_own(string? requested)
    {
        var callback = AuthEndpoints.SameOriginCallback(requested, "https://localhost:5179");

        Assert.Equal("https://localhost:5179/auth/callback", callback);
    }

    [Fact]
    public void A_same_origin_oauth_callback_is_preserved()
    {
        var callback = AuthEndpoints.SameOriginCallback(
            "https://localhost:5179/auth/callback?next=%2Fc", "https://localhost:5179");

        Assert.StartsWith("https://localhost:5179/auth/callback", callback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_administrator_cannot_delete_their_own_account()
    {
        var client = ClientAs(Roles.Government);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/foundation/whoami");
        var myId = me.GetProperty("data").GetProperty("id").GetString();

        var response = await client.DeleteAsync($"/api/auth/users/{myId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
