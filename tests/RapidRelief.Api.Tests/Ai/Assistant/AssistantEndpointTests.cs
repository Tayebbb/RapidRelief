using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Features.Ai.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 TEST PLAN item 8 — the /api/ai/assistant surface: authz, envelope shape, no-store,
/// owner scoping (D-048), the session cap, validation, and the §4.8 rule that a degraded
/// database must still produce an answer instead of a 503.
/// </summary>
public sealed class AssistantEndpointTests : IClassFixture<TestingWebAppFactory>
{
    private const string Base = "/api/ai/assistant";

    private static readonly Guid CitizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];
    private static readonly Guid RescueId = FakeAuthHandler.SeedUserIds[Roles.Rescue];

    private readonly TestingWebAppFactory _factory;

    public AssistantEndpointTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiDbContext>().AssistantMessages.ExecuteDeleteAsync();
    }

    private async Task SeedAsync(Guid userId, Guid sessionId, int count, DateTimeOffset start)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.AssistantMessages.Add(new AssistantMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = userId,
                Role = i % 2 == 0 ? AssistantRole.User : AssistantRole.Model,
                Text = $"seeded {i}",
                Provider = i % 2 == 0 ? null : "Canned",
                CreatedAtUtc = start.AddSeconds(i),
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAsync(Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>()
            .AssistantMessages.CountAsync(m => m.SessionId == sessionId);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body)
        => client.PostAsJsonAsync($"{Base}/messages", body);

    [Fact]
    public async Task Unauthenticated_post_is_rejected_with_401()
    {
        var response = await PostAsync(_factory.CreateClient(), new { message = "there is flooding near me" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", Base + "/sessions/11111111-1111-1111-1111-111111111111/messages")]
    [InlineData("DELETE", Base + "/sessions/11111111-1111-1111-1111-111111111111")]
    public async Task Unauthenticated_reads_and_deletes_are_rejected_with_401(string method, string url)
    {
        var response = await _factory.CreateClient()
            .SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Posting_a_message_answers_persists_both_turns_and_returns_the_documented_envelope()
    {
        await ResetAsync();

        var response = await PostAsync(Client(Roles.Citizen), new { message = "there is flooding near my house" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
        var data = await DataAsync(response);
        var sessionId = data.GetProperty("sessionId").GetGuid();
        Assert.NotEqual(Guid.Empty, sessionId);
        Assert.True(data.GetProperty("persisted").GetBoolean());
        Assert.False(data.GetProperty("degraded").GetBoolean());
        var answer = data.GetProperty("answer");
        // No GEMINI_API_KEY in Testing ⇒ the canned path, and it must still be a real answer.
        Assert.Equal("Canned", answer.GetProperty("provider").GetString());
        Assert.Contains("999", answer.GetProperty("text").GetString()!, StringComparison.Ordinal);
        Assert.False(answer.GetProperty("truncated").GetBoolean());
        Assert.True(answer.TryGetProperty("createdAtUtc", out _));
        Assert.Equal(2, await CountAsync(sessionId));
    }

    [Fact]
    public async Task A_returned_session_id_can_be_reused_and_the_history_reads_back_in_order()
    {
        await ResetAsync();
        var client = Client(Roles.Citizen);
        var first = await DataAsync(await PostAsync(client, new { message = "there is a fire in my building" }));
        var sessionId = first.GetProperty("sessionId").GetGuid();

        await PostAsync(client, new { sessionId, message = "the smoke is getting worse" });
        var response = await client.GetAsync($"{Base}/sessions/{sessionId}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
        var data = await DataAsync(response);
        Assert.Equal(sessionId, data.GetProperty("sessionId").GetGuid());
        var messages = data.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(4, messages.Count);
        Assert.Equal(new[] { "User", "Model", "User", "Model" },
            messages.Select(m => m.GetProperty("role").GetString()).ToArray());
        Assert.Equal("there is a fire in my building", messages[0].GetProperty("text").GetString());
        Assert.Equal("the smoke is getting worse", messages[2].GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Null, messages[0].GetProperty("provider").ValueKind);
        Assert.Equal("Canned", messages[1].GetProperty("provider").GetString());
        Assert.True(messages[0].TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Another_users_session_id_returns_an_empty_history_never_their_messages()
    {
        await ResetAsync();
        var sessionId = Guid.NewGuid();
        await SeedAsync(CitizenId, sessionId, count: 4, DateTimeOffset.UtcNow.AddMinutes(-5));

        var data = await DataAsync(await Client(Roles.Rescue).GetAsync($"{Base}/sessions/{sessionId}/messages"));

        Assert.Empty(data.GetProperty("messages").EnumerateArray());
        Assert.Equal(4, await CountAsync(sessionId)); // and nothing was leaked or destroyed
    }

    [Fact]
    public async Task Deleting_a_session_is_owner_scoped_and_idempotent()
    {
        await ResetAsync();
        var sessionId = Guid.NewGuid();
        await SeedAsync(CitizenId, sessionId, count: 4, DateTimeOffset.UtcNow.AddMinutes(-5));

        var foreignDelete = await Client(Roles.Rescue).DeleteAsync($"{Base}/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NoContent, foreignDelete.StatusCode);
        Assert.Equal(4, await CountAsync(sessionId));

        var ownDelete = await Client(Roles.Citizen).DeleteAsync($"{Base}/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);
        Assert.Equal(0, await CountAsync(sessionId));

        var repeat = await Client(Roles.Citizen).DeleteAsync($"{Base}/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
    }

    [Fact]
    public async Task A_forged_session_id_owned_by_nobody_starts_an_empty_conversation()
    {
        await ResetAsync();
        var foreign = Guid.NewGuid();
        await SeedAsync(RescueId, foreign, count: 2, DateTimeOffset.UtcNow.AddMinutes(-5));

        var data = await DataAsync(await PostAsync(Client(Roles.Citizen),
            new { sessionId = foreign, message = "what should I do" }));

        Assert.Equal(foreign, data.GetProperty("sessionId").GetGuid());
        Assert.True(data.GetProperty("persisted").GetBoolean());
        // The caller's two new rows sit alongside — never inside — the other user's history.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        Assert.Equal(2, await db.AssistantMessages.CountAsync(m => m.SessionId == foreign && m.UserId == CitizenId));
        Assert.Equal(2, await db.AssistantMessages.CountAsync(m => m.SessionId == foreign && m.UserId == RescueId));
    }

    [Fact]
    public async Task A_full_session_is_refused_with_400_and_persists_nothing()
    {
        await ResetAsync();
        var sessionId = Guid.NewGuid();
        await SeedAsync(CitizenId, sessionId, count: 50, DateTimeOffset.UtcNow.AddMinutes(-10));

        var response = await PostAsync(Client(Roles.Citizen), new { sessionId, message = "one more question" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(50, await CountAsync(sessionId));
    }

    public static TheoryData<string> InvalidRequests => new()
    {
        """{"message":""}""",
        """{"message":"   "}""",
        """{"message":null}""",
        "{\"message\":\"" + new string('x', 1001) + "\"}",
        """{"message":"help","latitude":91,"longitude":90}""",
        """{"message":"help","latitude":23.8,"longitude":181}""",
        """{"message":"help","longitude":90.4}""",
        """{"message":"help","latitude":23.8}""",
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_requests_are_rejected_with_400(string json)
    {
        var response = await Client(Roles.Citizen).PostAsync($"{Base}/messages",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_message_at_the_length_limit_is_accepted()
    {
        await ResetAsync();

        var response = await PostAsync(Client(Roles.Citizen), new { message = new string('x', 1000) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Coordinates_are_accepted_and_never_stored_with_the_message()
    {
        await ResetAsync();

        var data = await DataAsync(await PostAsync(Client(Roles.Citizen),
            new { message = "where is the nearest shelter", latitude = 23.8103, longitude = 90.4125 }));

        Assert.True(data.GetProperty("persisted").GetBoolean());
        using var scope = _factory.Services.CreateScope();
        var texts = await scope.ServiceProvider.GetRequiredService<AiDbContext>()
            .AssistantMessages.Select(m => m.Text).ToListAsync();
        Assert.DoesNotContain(texts, t => t.Contains("23.8103", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_degraded_database_still_answers_statelessly_and_never_returns_503()
    {
        await ResetAsync();
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        try
        {
            health.PostgresAvailable = false;

            var response = await PostAsync(Client(Roles.Citizen), new { message = "there is flooding near me" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = await DataAsync(response);
            Assert.Equal(JsonValueKind.Null, data.GetProperty("sessionId").ValueKind);
            Assert.True(data.GetProperty("degraded").GetBoolean());
            Assert.False(data.GetProperty("persisted").GetBoolean());
            Assert.Contains("999", data.GetProperty("answer").GetProperty("text").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            health.PostgresAvailable = true;
        }
    }

    [Fact]
    public async Task Degraded_reads_and_deletes_return_503()
    {
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        var client = Client(Roles.Citizen);
        try
        {
            health.PostgresAvailable = false;

            var get = await client.GetAsync($"{Base}/sessions/{Guid.NewGuid()}/messages");
            var delete = await client.DeleteAsync($"{Base}/sessions/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, get.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
            Assert.Equal("application/problem+json", get.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            health.PostgresAvailable = true;
        }
    }

    [Fact]
    public async Task History_is_capped_at_the_session_maximum()
    {
        await ResetAsync();
        var sessionId = Guid.NewGuid();
        await SeedAsync(CitizenId, sessionId, count: 50, DateTimeOffset.UtcNow.AddMinutes(-10));

        var data = await DataAsync(await Client(Roles.Citizen).GetAsync($"{Base}/sessions/{sessionId}/messages"));

        Assert.Equal(50, data.GetProperty("messages").EnumerateArray().Count());
    }

    [Fact]
    public void The_post_uses_the_assistant_budget_while_the_reads_stay_on_the_ai_budget()
    {
        var endpoints = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.Contains("api/ai/assistant", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            var policy = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName;
            var isPost = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST");
            Assert.Equal(isPost ? "assistant" : "ai", policy);
        }
    }
}
