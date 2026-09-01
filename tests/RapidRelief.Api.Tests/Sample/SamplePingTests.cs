using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Tests.Sample;

/// <summary>D-008 Sample slice end-to-end via the SQLite-backed factory — no Postgres involved.</summary>
public sealed class SamplePingTests : IClassFixture<TestingWebAppFactory>
{
    private const string PingsRoute = "/api/sample/pings";

    private readonly TestingWebAppFactory _factory;

    public SamplePingTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient CreateClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    [Fact]
    public async Task Post_valid_ping_as_admin_returns_201_with_envelope_and_location_header()
    {
        var client = CreateClientWithRole(Roles.Admin);

        var response = await client.PostAsJsonAsync(PingsRoute, new { message = "hello from chunk 2" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("hello from chunk 2", data.GetProperty("message").GetString());
        Assert.True(data.TryGetProperty("createdAtUtc", out _));
        Assert.Equal($"{PingsRoute}/{id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Get_pings_anonymously_returns_paged_envelope_containing_posted_ping()
    {
        var admin = CreateClientWithRole(Roles.Admin);
        var posted = await admin.PostAsJsonAsync(PingsRoute, new { message = "find me in the page" });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);

        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync(PingsRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.True(data.GetProperty("pageSize").GetInt32() > 0);
        Assert.True(data.GetProperty("totalCount").GetInt32() >= 1);
        var messages = data.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("message").GetString())
            .ToList();
        Assert.Contains("find me in the page", messages);
    }

    [Fact]
    public async Task Post_empty_message_returns_400_problem_details_with_field_error()
    {
        var client = CreateClientWithRole(Roles.Admin);

        var response = await client.PostAsJsonAsync(PingsRoute, new { message = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Message", out var fieldErrors));
        Assert.True(fieldErrors.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Post_message_longer_than_500_chars_returns_400_problem_details()
    {
        var client = CreateClientWithRole(Roles.Admin);

        var response = await client.PostAsJsonAsync(PingsRoute, new { message = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Message", out _));
    }

    [Fact]
    public async Task Post_as_citizen_returns_403()
    {
        var client = CreateClientWithRole(Roles.Citizen);

        var response = await client.PostAsJsonAsync(PingsRoute, new { message = "not an admin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_unauthenticated_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(PingsRoute, new { message = "who am I" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PingCreated_event_reaches_test_registered_probe_handler()
    {
        var probe = new PingCreatedProbe();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<IEventHandler<PingCreated>, ProbeForwardingHandler>();
            }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Admin);

        var response = await client.PostAsJsonAsync(PingsRoute, new { message = "probe me" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var received = Assert.Single(probe.Received);
        Assert.Equal("probe me", received.Message);
        Assert.NotEqual(Guid.Empty, received.PingId);
    }

    private sealed class PingCreatedProbe
    {
        public List<PingCreated> Received { get; } = [];
    }

    private sealed class ProbeForwardingHandler : IEventHandler<PingCreated>
    {
        private readonly PingCreatedProbe _probe;

        public ProbeForwardingHandler(PingCreatedProbe probe) => _probe = probe;

        public Task HandleAsync(PingCreated evt, CancellationToken ct = default)
        {
            _probe.Received.Add(evt);
            return Task.CompletedTask;
        }
    }
}
