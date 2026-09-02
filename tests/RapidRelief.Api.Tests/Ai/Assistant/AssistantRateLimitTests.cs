using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// D-054 — one POST is one live OpenRouter call, so the assistant runs on its own tight per-user
/// budget. Development registers the limiter (it is skipped in Testing); the empty connection
/// string boots degraded, which is exactly the "must still answer 200" path.
/// </summary>
public sealed class AssistantRateLimitTests
{
    private const string Route = "/api/ai/assistant/messages";

    [Fact]
    public async Task The_post_budget_is_enforced_per_user_and_does_not_spill_onto_another_caller()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", "");
            builder.UseSetting("RateLimiting:Assistant:PermitLimit", "2");
            builder.UseSetting("RateLimiting:Assistant:WindowSeconds", "300");
        });

        using var citizen = ClientAs(factory, Roles.Citizen);
        using var rescue = ClientAs(factory, Roles.Rescue);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await PostAsync(citizen)).StatusCode);
        }

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }, statuses);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostAsync(rescue)).StatusCode);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client)
        => client.PostAsync(Route, new StringContent(
            """{"message":"there is flooding near my house"}""", Encoding.UTF8, "application/json"));

    private static HttpClient ClientAs(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }
}
