using System.Net.Http.Json;
using System.Text.Json;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;
using ClientModels = RapidRelief.Client.Features.Assistant;
using ServerWire = RapidRelief.Api.Features.Ai.Endpoints;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// The client hand-mirrors F16's wire records and hardcodes the route (D-019/D-045). Nothing in
/// the client build can catch a server-side rename, so these pins are the drift alarm: rename a
/// field or move the route and the server suite goes red.
/// </summary>
public sealed class AssistantWireContractTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public AssistantWireContractTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public void The_route_constant_is_the_literal_the_client_hardcodes()
        => Assert.Equal(ServerWire.AssistantEndpoints.BasePath, "/" + ClientModels.AssistantApi.BasePath);

    [Theory]
    [InlineData(typeof(ServerWire.AssistantMessageRequest), typeof(ClientModels.AssistantMessageRequest))]
    [InlineData(typeof(ServerWire.AssistantAnswerDto), typeof(ClientModels.AssistantAnswerDto))]
    [InlineData(typeof(ServerWire.AssistantMessageResponse), typeof(ClientModels.AssistantMessageResponse))]
    [InlineData(typeof(ServerWire.AssistantMessageDto), typeof(ClientModels.AssistantMessageDto))]
    [InlineData(typeof(ServerWire.AssistantHistoryResponse), typeof(ClientModels.AssistantHistoryResponse))]
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
    public async Task The_answer_and_history_json_carry_exactly_the_fields_the_client_binds()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Citizen);

        using var posted = await client.PostAsJsonAsync(
            $"{ServerWire.AssistantEndpoints.BasePath}/messages", new { message = "there is flooding near me" });
        var answerData = await DataAsync(posted);
        var sessionId = answerData.GetProperty("sessionId").GetGuid();
        var historyData = await DataAsync(await client.GetAsync(
            $"{ServerWire.AssistantEndpoints.BasePath}/sessions/{sessionId:D}/messages"));

        Assert.Equal(["answer", "degraded", "persisted", "sessionId"], PropertyNames(answerData));
        Assert.Equal(["createdAtUtc", "provider", "text", "truncated"],
            PropertyNames(answerData.GetProperty("answer")));
        Assert.Equal(["messages", "sessionId"], PropertyNames(historyData));
        Assert.Equal(["createdAtUtc", "id", "provider", "role", "text"],
            PropertyNames(historyData.GetProperty("messages").EnumerateArray().First()));
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();

    private static IReadOnlyList<string> PropertyNames(JsonElement element)
        => element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static string Readable(Type type)
        => type.IsGenericType
            ? $"{type.Name}<{string.Join(",", type.GetGenericArguments().Select(Readable))}>"
            : type.Name;
}
