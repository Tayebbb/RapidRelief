using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Features.Realtime.Endpoints;
using RapidRelief.Api.Features.Realtime.Pipeline;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// D-038/D-039 — inbox paging, audience filtering (fan-out read state is the easiest thing to
/// get subtly wrong) and the read surfaces. Every response must be no-store.
/// </summary>
public sealed class NotificationEndpointTests : IClassFixture<TestingWebAppFactory>
{
    private const string Base = "/api/realtime/notifications";

    // Relative to the live clock on purpose: retention-style filters compare against "now", so a
    // hardcoded calendar date would turn this suite red on a future day instead of on a defect.
    private static readonly DateTimeOffset Anchor = DateTimeOffset.UtcNow.AddHours(-1);

    private readonly TestingWebAppFactory _factory;

    public NotificationEndpointTests(TestingWebAppFactory factory) => _factory = factory;

    private Task ResetAsync() => RealtimeTestSupport.ResetAsync(_factory.Services);

    private HttpClient Client(string role) => RealtimeTestSupport.ClientWithRole(_factory, role);

    private Task<Guid> SeedAsync(string audience, string? role, Guid? userId, string topic, DateTimeOffset createdAt)
        => RealtimeTestSupport.SeedAsync(_factory.Services, audience, role, userId, topic, createdAt);

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.GetProperty("data").Clone();
    }

    private static IReadOnlyList<string> Topics(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("topic").GetString()!).ToList();

    [Theory]
    [InlineData("GET", Base)]
    [InlineData("GET", Base + "/unread-count")]
    [InlineData("POST", Base + "/read-all")]
    public async Task Unauthenticated_requests_are_rejected_with_401(string method, string url)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_mark_read_is_rejected_with_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PatchAsync($"{Base}/{Guid.NewGuid()}/read", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Inbox_returns_an_envelope_with_no_store_and_the_documented_shape()
    {
        await ResetAsync();
        await SeedAsync(NotificationAudience.All, null, null, "alerts.published", Anchor);

        var response = await Client(Roles.Citizen).GetAsync(Base);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
        var data = await DataAsync(response);
        var item = Assert.Single(data.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("alerts.published", item.GetProperty("topic").GetString());
        Assert.Equal("All", item.GetProperty("audience").GetString());
        Assert.False(item.GetProperty("isRead").GetBoolean());
        Assert.True(data.TryGetProperty("serverTimeUtc", out _));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("nextCursor").GetString()));
    }

    [Fact]
    public async Task Audience_filtering_hides_other_roles_and_other_users()
    {
        await ResetAsync();
        await SeedAsync(NotificationAudience.All, null, null, "topic.all", Anchor);
        await SeedAsync(NotificationAudience.Role, Roles.Rescue, null, "topic.rescue", Anchor.AddSeconds(1));
        await SeedAsync(NotificationAudience.Role, Roles.Admin, null, "topic.admin", Anchor.AddSeconds(2));
        await SeedAsync(NotificationAudience.User, null, RealtimeTestSupport.CitizenId, "topic.mine", Anchor.AddSeconds(3));
        await SeedAsync(NotificationAudience.User, null, Guid.NewGuid(), "topic.someone-else", Anchor.AddSeconds(4));

        var citizen = Topics(await DataAsync(await Client(Roles.Citizen).GetAsync(Base)));
        var rescue = Topics(await DataAsync(await Client(Roles.Rescue).GetAsync(Base)));

        Assert.Equal(new[] { "topic.all", "topic.mine" }, citizen);
        Assert.Equal(new[] { "topic.all", "topic.rescue" }, rescue);
    }

    [Fact]
    public async Task Items_are_returned_oldest_first()
    {
        await ResetAsync();
        await SeedAsync(NotificationAudience.All, null, null, "topic.second", Anchor.AddMinutes(1));
        await SeedAsync(NotificationAudience.All, null, null, "topic.first", Anchor);

        var topics = Topics(await DataAsync(await Client(Roles.Citizen).GetAsync(Base)));

        Assert.Equal(new[] { "topic.first", "topic.second" }, topics);
    }

    [Fact]
    public async Task Without_a_cursor_the_newest_rows_win_the_limit()
    {
        await ResetAsync();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(NotificationAudience.All, null, null, $"topic.{i}", Anchor.AddMinutes(i));
        }

        var topics = Topics(await DataAsync(await Client(Roles.Citizen).GetAsync($"{Base}?limit=2")));

        Assert.Equal(new[] { "topic.3", "topic.4" }, topics);
    }

    [Fact]
    public async Task Limit_is_clamped_between_one_and_one_hundred()
    {
        await ResetAsync();
        for (var i = 0; i < 105; i++)
        {
            await SeedAsync(NotificationAudience.All, null, null, $"topic.{i:D3}", Anchor.AddSeconds(i));
        }
        var client = Client(Roles.Citizen);

        var tooSmall = await DataAsync(await client.GetAsync($"{Base}?limit=0"));
        var tooLarge = await DataAsync(await client.GetAsync($"{Base}?limit=1000"));
        var negative = await DataAsync(await client.GetAsync($"{Base}?limit=-5"));

        Assert.Single(tooSmall.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(NotificationEndpoints.MaxLimit, tooLarge.GetProperty("items").EnumerateArray().Count());
        Assert.Single(negative.GetProperty("items").EnumerateArray().ToList());
    }

    [Fact]
    public async Task Cursor_paging_walks_every_row_exactly_once_even_when_rows_share_a_tick()
    {
        await ResetAsync();
        var sameTick = Anchor.AddMinutes(5);
        var expected = new List<Guid>
        {
            await SeedAsync(NotificationAudience.All, null, null, "tick.a", sameTick),
            await SeedAsync(NotificationAudience.All, null, null, "tick.b", sameTick),
            await SeedAsync(NotificationAudience.All, null, null, "tick.c", sameTick),
            await SeedAsync(NotificationAudience.All, null, null, "tick.d", sameTick.AddTicks(1)),
        };
        var client = Client(Roles.Citizen);

        var seen = new List<Guid>();
        var cursor = NotificationCursor.Encode(sameTick.AddTicks(-1), Guid.Empty);
        for (var page = 0; page < 10; page++)
        {
            var data = await DataAsync(await client.GetAsync($"{Base}?limit=1&since={Uri.EscapeDataString(cursor)}"));
            var ids = data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
            if (ids.Count == 0)
            {
                break;
            }
            seen.AddRange(ids);
            cursor = data.GetProperty("nextCursor").GetString()!;
        }

        Assert.Equal(expected.Count, seen.Count);
        Assert.Equal(expected.Count, seen.Distinct().Count());
        Assert.Equal(expected.OrderBy(id => id).ToList(), seen.OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task A_same_tick_burst_larger_than_the_page_cap_stays_bounded()
    {
        await ResetAsync();
        var sameTick = Anchor.AddMinutes(2);
        for (var i = 0; i < NotificationEndpoints.MaxLimit + 5; i++)
        {
            await SeedAsync(NotificationAudience.All, null, null, $"burst.{i:D3}", sameTick);
        }

        var data = await DataAsync(await Client(Roles.Citizen).GetAsync($"{Base}?limit={NotificationEndpoints.MaxLimit}"));

        Assert.Equal(NotificationEndpoints.MaxLimit, data.GetProperty("items").EnumerateArray().Count());
    }

    [Fact]
    public async Task An_undecodable_cursor_is_a_400()
    {
        await ResetAsync();

        var response = await Client(Roles.Citizen).GetAsync($"{Base}?since=not-a-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_exhausted_cursor_returns_no_items_and_echoes_the_cursor()
    {
        await ResetAsync();
        await SeedAsync(NotificationAudience.All, null, null, "topic.only", Anchor);
        var cursor = NotificationCursor.Encode(Anchor.AddDays(1), Guid.Empty);

        var data = await DataAsync(await Client(Roles.Citizen).GetAsync($"{Base}?since={Uri.EscapeDataString(cursor)}"));

        Assert.Empty(data.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(cursor, data.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task Marking_read_is_idempotent_and_reflected_in_the_inbox()
    {
        await ResetAsync();
        var id = await SeedAsync(NotificationAudience.All, null, null, "topic.read-me", Anchor);
        var client = Client(Roles.Citizen);

        var first = await client.PatchAsync($"{Base}/{id}/read", content: null);
        var second = await client.PatchAsync($"{Base}/{id}/read", content: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var data = await DataAsync(await client.GetAsync(Base));
        Assert.True(data.GetProperty("items")[0].GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task Read_state_is_per_user()
    {
        await ResetAsync();
        var id = await SeedAsync(NotificationAudience.All, null, null, "topic.shared", Anchor);
        await Client(Roles.Citizen).PatchAsync($"{Base}/{id}/read", content: null);

        var rescueInbox = await DataAsync(await Client(Roles.Rescue).GetAsync(Base));

        Assert.False(rescueInbox.GetProperty("items")[0].GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task Marking_an_invisible_or_unknown_notification_read_is_a_404()
    {
        await ResetAsync();
        var adminOnly = await SeedAsync(NotificationAudience.Role, Roles.Admin, null, "topic.admin-only", Anchor);
        var client = Client(Roles.Citizen);

        var invisible = await client.PatchAsync($"{Base}/{adminOnly}/read", content: null);
        var unknown = await client.PatchAsync($"{Base}/{Guid.NewGuid()}/read", content: null);

        Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Read_all_marks_only_visible_unread_rows_and_reports_the_count()
    {
        await ResetAsync();
        await SeedAsync(NotificationAudience.All, null, null, "topic.a", Anchor);
        await SeedAsync(NotificationAudience.User, null, RealtimeTestSupport.CitizenId, "topic.b", Anchor.AddSeconds(1));
        await SeedAsync(NotificationAudience.Role, Roles.Admin, null, "topic.c", Anchor.AddSeconds(2));
        var client = Client(Roles.Citizen);

        var response = await client.PostAsync($"{Base}/read-all", content: null);
        var again = await client.PostAsync($"{Base}/read-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await DataAsync(response)).GetProperty("marked").GetInt32());
        Assert.Equal(0, (await DataAsync(again)).GetProperty("marked").GetInt32());
        var admin = await DataAsync(await Client(Roles.Admin).GetAsync(Base));
        Assert.All(admin.GetProperty("items").EnumerateArray(), i => Assert.False(i.GetProperty("isRead").GetBoolean()));
    }

    [Fact]
    public async Task Unread_count_matches_the_audience_predicate()
    {
        await ResetAsync();
        await SeedAsync(NotificationAudience.All, null, null, "topic.a", Anchor);
        var mine = await SeedAsync(NotificationAudience.User, null, RealtimeTestSupport.CitizenId, "topic.b", Anchor.AddSeconds(1));
        await SeedAsync(NotificationAudience.Role, Roles.Rescue, null, "topic.c", Anchor.AddSeconds(2));
        var client = Client(Roles.Citizen);

        var before = await DataAsync(await client.GetAsync($"{Base}/unread-count"));
        await client.PatchAsync($"{Base}/{mine}/read", content: null);
        var after = await DataAsync(await client.GetAsync($"{Base}/unread-count"));

        Assert.Equal(2, before.GetProperty("count").GetInt32());
        Assert.Equal(1, after.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task An_old_unread_row_is_both_listed_and_counted()
    {
        await ResetAsync();
        await SeedAsync(NotificationAudience.All, null, null, "topic.ancient", DateTimeOffset.UtcNow.AddDays(-400));
        var client = Client(Roles.Citizen);

        var listed = Topics(await DataAsync(await client.GetAsync(Base)));
        var count = (await DataAsync(await client.GetAsync($"{Base}/unread-count"))).GetProperty("count").GetInt32();

        Assert.Equal(new[] { "topic.ancient" }, listed);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Every_endpoint_returns_503_while_the_database_is_degraded()
    {
        await ResetAsync();
        var client = Client(Roles.Citizen);
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        List<HttpResponseMessage> responses;
        try
        {
            health.PostgresAvailable = false;
            responses =
            [
                await client.GetAsync(Base),
                await client.GetAsync($"{Base}/unread-count"),
                await client.PostAsync($"{Base}/read-all", content: null),
                await client.PatchAsync($"{Base}/{Guid.NewGuid()}/read", content: null),
            ];
        }
        finally
        {
            health.PostgresAvailable = true;
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode));
        Assert.All(responses, r => Assert.Equal("no-store, private", r.Headers.CacheControl?.ToString()));
    }
}
