using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// Post-review item 1 — the POST must NEVER return 5xx. DatabaseHealth.PostgresAvailable is
/// only set at startup, so a process that booted healthy and later lost its database is the
/// realistic failure: every DB/context touchpoint has to degrade instead of throwing.
/// The factory here is class-scoped, so the destructive schema tricks touch nobody else.
/// </summary>
public sealed class AssistantDatabaseFailureTests : IClassFixture<TestingWebAppFactory>
{
    private const string Base = "/api/ai/assistant";
    private const string Table = "ai_assistant_messages";

    private static readonly Guid CitizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];

    private readonly TestingWebAppFactory _factory;

    public AssistantDatabaseFailureTests(TestingWebAppFactory factory) => _factory = factory;

    /// <summary>Every read AND write against the assistant table fails — "Postgres went away".</summary>
    private sealed class HiddenTable : IDisposable
    {
        private readonly IServiceProvider _services;

        public HiddenTable(IServiceProvider services)
        {
            _services = services;
            Execute($"ALTER TABLE {Table} RENAME TO {Table}_hidden");
        }

        public void Dispose() => Execute($"ALTER TABLE {Table}_hidden RENAME TO {Table}");

        private void Execute(string sql)
        {
            using var scope = _services.CreateScope();
            scope.ServiceProvider.GetRequiredService<AiDbContext>().Database.ExecuteSqlRaw(sql);
        }
    }

    /// <summary>Reads keep working; only the persist fails — isolates the post-answer write.</summary>
    private sealed class BlockedInserts : IDisposable
    {
        private const string TriggerName = "assistant_insert_blocker";

        private readonly IServiceProvider _services;

        public BlockedInserts(IServiceProvider services)
        {
            _services = services;
            Execute($"CREATE TRIGGER {TriggerName} BEFORE INSERT ON {Table} " +
                    "BEGIN SELECT RAISE(FAIL, 'simulated write failure'); END");
        }

        public void Dispose() => Execute($"DROP TRIGGER {TriggerName}");

        private void Execute(string sql)
        {
            using var scope = _services.CreateScope();
            scope.ServiceProvider.GetRequiredService<AiDbContext>().Database.ExecuteSqlRaw(sql);
        }
    }

    private sealed class ThrowingShelterReadService : IShelterReadService
    {
        public Task<IReadOnlyList<ShelterSummaryDto>> GetSheltersAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("shelter read service is down");

        public Task<IReadOnlyList<ShelterSummaryDto>> GetNearestAsync(
            GeoPoint origin, int count = 5, CancellationToken ct = default)
            => throw new InvalidOperationException("shelter read service is down");
    }

    private HttpClient Client(WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Citizen);
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiDbContext>().AssistantMessages.ExecuteDeleteAsync();
    }

    private async Task SeedAsync(Guid sessionId, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        for (var i = 0; i < count; i++)
        {
            db.AssistantMessages.Add(new AssistantMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = CitizenId,
                Role = i % 2 == 0 ? AssistantRole.User : AssistantRole.Model,
                Text = $"seeded {i}",
                Provider = i % 2 == 0 ? null : "Canned",
                CreatedAtUtc = start.AddSeconds(i),
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>().AssistantMessages.CountAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body)
        => client.PostAsJsonAsync($"{Base}/messages", body);

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();

    private static void AssertUsableAnswer(JsonElement data)
    {
        var text = data.GetProperty("answer").GetProperty("text").GetString()!;
        Assert.Contains("999", text, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("answer").GetProperty("provider").GetString()));
    }

    [Fact]
    public async Task A_database_that_dies_after_startup_still_answers_with_200_instead_of_500()
    {
        await ResetAsync();
        using var down = new HiddenTable(_factory.Services);

        var response = await PostAsync(Client(), new { message = "there is flooding near my house" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("sessionId").ValueKind);
        Assert.True(data.GetProperty("degraded").GetBoolean());
        Assert.False(data.GetProperty("persisted").GetBoolean());
        AssertUsableAnswer(data);
    }

    [Fact]
    public async Task A_history_read_that_throws_degrades_to_an_empty_conversation_never_a_500()
    {
        await ResetAsync();
        var sessionId = Guid.NewGuid();
        // 50 rows would normally trip the "conversation full" 400 — an unreadable history
        // must not fail closed on a cap it can no longer see.
        await SeedAsync(sessionId, count: 50);

        using (new HiddenTable(_factory.Services))
        {
            var response = await PostAsync(Client(), new { sessionId, message = "the water is rising" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertUsableAnswer(await DataAsync(response));
        }

        Assert.Equal(50, await CountAsync()); // nothing was lost while the table was away
    }

    [Fact]
    public async Task A_persist_that_throws_after_the_answer_exists_returns_the_answer_as_unpersisted()
    {
        await ResetAsync();
        var sessionId = Guid.NewGuid();
        await SeedAsync(sessionId, count: 2);

        using (new BlockedInserts(_factory.Services))
        {
            var response = await PostAsync(Client(), new { sessionId, message = "should I evacuate" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = await DataAsync(response);
            // The answer exists, so it is delivered — but the client must not believe it was saved.
            Assert.Equal(JsonValueKind.Null, data.GetProperty("sessionId").ValueKind);
            Assert.True(data.GetProperty("degraded").GetBoolean());
            Assert.False(data.GetProperty("persisted").GetBoolean());
            AssertUsableAnswer(data);
        }

        Assert.Equal(2, await CountAsync()); // the read half worked; neither new row landed
    }

    [Fact]
    public async Task A_shelter_read_service_that_throws_answers_without_shelter_context()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IShelterReadService>();
            services.AddSingleton<IShelterReadService, ThrowingShelterReadService>();
        }));

        var response = await PostAsync(Client(factory),
            new { message = "where is the nearest shelter", latitude = 23.8103, longitude = 90.4125 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await DataAsync(response);
        Assert.True(data.GetProperty("persisted").GetBoolean()); // only the context was lost
        AssertUsableAnswer(data);
    }
}
