using System.Net;
using System.Text;
using System.Text.Json;
using RapidRelief.Client.Features.Assistant;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 chunk 2 client transport. The Client assembly flows here transitively (the Api hosts it),
/// so the rule that matters most — the chat NEVER dead-ends and never throws, whatever the
/// server or the network does — is covered by real tests despite there being no bUnit.
/// </summary>
public sealed class AssistantApiTests
{
    private const string MessagesUrl = "http://localhost/api/ai/assistant/messages";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond(request, LastBody);
        }
    }

    private static (AssistantApi Api, StubHandler Handler) ApiThat(
        Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return (new AssistantApi(http), handler);
    }

    private static (AssistantApi Api, StubHandler Handler) ApiReturning(HttpStatusCode status, string body = "")
        => ApiThat((_, _) => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    private static (AssistantApi Api, StubHandler Handler) ApiThrowing(Exception exception)
        => ApiThat((_, _) => throw exception);

    private static HttpResponseMessage Envelope<T>(T data, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new ApiEnvelope<T>(data), Web), Encoding.UTF8, "application/json"),
        };

    private static AssistantMessageResponse ServerAnswer(Guid sessionId, string text = "Move to higher ground.")
        => new(sessionId, new AssistantAnswerDto(text, "Gemini", Truncated: false, DateTimeOffset.UtcNow),
            Degraded: false, Persisted: true);

    [Fact]
    public async Task A_successful_send_returns_the_servers_answer_and_session()
    {
        var sessionId = Guid.NewGuid();
        var (api, _) = ApiThat((_, _) => Envelope(ServerAnswer(sessionId)));

        var result = await api.SendAsync(null, "the water is rising", null, null);

        Assert.False(result.IsFallback);
        Assert.Null(result.Notice);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("Move to higher ground.", result.Answer.Text);
        Assert.Equal("Gemini", result.Answer.Provider);
        Assert.True(result.Persisted);
        Assert.False(result.Degraded);
    }

    [Fact]
    public async Task The_request_posts_only_the_session_message_and_coordinates_never_history()
    {
        var sessionId = Guid.NewGuid();
        var (api, handler) = ApiThat((_, _) => Envelope(ServerAnswer(sessionId)));

        await api.SendAsync(sessionId, "smoke in the stairwell", 23.81, 90.41);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(MessagesUrl, handler.LastRequest.RequestUri!.ToString());
        var body = JsonDocument.Parse(handler.LastBody!).RootElement;
        Assert.Equal(
            ["latitude", "longitude", "message", "sessionId"],
            body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(sessionId, body.GetProperty("sessionId").GetGuid());
        Assert.Equal("smoke in the stairwell", body.GetProperty("message").GetString());
        Assert.Equal(23.81, body.GetProperty("latitude").GetDouble());
        Assert.Equal(90.41, body.GetProperty("longitude").GetDouble());
    }

    [Fact]
    public async Task Coordinates_are_null_when_the_user_has_not_shared_a_location()
    {
        var (api, handler) = ApiThat((_, _) => Envelope(ServerAnswer(Guid.NewGuid())));

        await api.SendAsync(null, "what should I pack", null, null);

        var body = JsonDocument.Parse(handler.LastBody!).RootElement;
        Assert.Equal(JsonValueKind.Null, body.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("longitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sessionId").ValueKind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Every_failure_status_yields_the_local_fallback_line_instead_of_an_exception(
        HttpStatusCode status)
    {
        var (api, _) = ApiReturning(status);

        var result = await api.SendAsync(null, "there is a fire", null, null);

        Assert.True(result.IsFallback);
        Assert.Equal(AssistantApi.FallbackText, result.Answer.Text);
        Assert.Contains("999", result.Answer.Text, StringComparison.Ordinal);
        Assert.Null(result.SessionId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task The_statuses_a_user_can_act_on_carry_a_one_line_notice(HttpStatusCode status)
    {
        var (api, _) = ApiReturning(status);

        var result = await api.SendAsync(null, "there is a fire", null, null);

        Assert.False(string.IsNullOrWhiteSpace(result.Notice));
    }

    [Fact]
    public async Task A_validation_problem_surfaces_the_servers_own_detail_as_the_notice()
    {
        const string detail = "This conversation has reached its limit of 50 messages. Start a new chat.";
        var (api, _) = ApiReturning(HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new { title = "Conversation full", detail }, Web));

        var result = await api.SendAsync(Guid.NewGuid(), "one more question", null, null);

        Assert.True(result.IsFallback);
        Assert.Equal(detail, result.Notice);
    }

    [Fact]
    public async Task A_validation_problem_without_a_detail_still_explains_itself()
    {
        var (api, _) = ApiReturning(HttpStatusCode.BadRequest, "{\"errors\":{\"Message\":[\"too long\"]}}");

        var result = await api.SendAsync(null, "x", null, null);

        Assert.True(result.IsFallback);
        Assert.False(string.IsNullOrWhiteSpace(result.Notice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"sessionId\":null}}")]
    public async Task A_success_status_with_an_unusable_body_falls_back(string body)
    {
        var (api, _) = ApiReturning(HttpStatusCode.OK, body);

        var result = await api.SendAsync(null, "there is a fire", null, null);

        Assert.True(result.IsFallback);
        Assert.Equal(AssistantApi.FallbackText, result.Answer.Text);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task A_transport_failure_falls_back_instead_of_throwing(Exception failure)
    {
        var (api, _) = ApiThrowing(failure);

        var result = await api.SendAsync(null, "there is a fire", null, null);

        Assert.True(result.IsFallback);
        Assert.Equal(AssistantApi.FallbackText, result.Answer.Text);
    }

    public static TheoryData<Exception> TransportFailures() =>
    [
        new HttpRequestException("offline"),
        new TaskCanceledException("timeout"),
        new OperationCanceledException("cancelled"),
        new JsonException("garbage"),
    ];

    [Fact]
    public async Task The_fallback_answer_is_marked_as_non_gemini_so_the_page_can_flag_it()
    {
        var (api, _) = ApiThrowing(new HttpRequestException("offline"));

        var result = await api.SendAsync(null, "there is a fire", null, null);

        Assert.NotEqual("Gemini", result.Answer.Provider);
        Assert.False(result.Persisted);
    }

    [Fact]
    public async Task History_is_read_from_the_session_route_and_parsed()
    {
        var sessionId = Guid.NewGuid();
        var messages = new List<AssistantMessageDto>
        {
            new(Guid.NewGuid(), "User", "is the road flooded", null, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "Model", "Avoid the road.", "Canned", DateTimeOffset.UtcNow),
        };
        var (api, handler) = ApiThat((_, _) => Envelope(new AssistantHistoryResponse(sessionId, messages)));

        var history = await api.GetHistoryAsync(sessionId);

        Assert.Equal($"http://localhost/api/ai/assistant/sessions/{sessionId:D}/messages",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal(2, history!.Messages.Count);
        Assert.Equal("Canned", history.Messages[1].Provider);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_unreadable_history_is_simply_no_history(HttpStatusCode status)
    {
        var (api, _) = ApiReturning(status);

        Assert.Null(await api.GetHistoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_transport_failure_while_reading_history_is_no_history()
    {
        var (api, _) = ApiThrowing(new HttpRequestException("offline"));

        Assert.Null(await api.GetHistoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_a_session_targets_the_session_route_and_reports_success()
    {
        var sessionId = Guid.NewGuid();
        var (api, handler) = ApiReturning(HttpStatusCode.NoContent);

        Assert.True(await api.DeleteSessionAsync(sessionId));
        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Equal($"http://localhost/api/ai/assistant/sessions/{sessionId:D}",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task A_confirmed_delete_reports_success_so_the_page_may_clear_the_thread(HttpStatusCode status)
    {
        var (api, _) = ApiReturning(status);

        Assert.True(await api.DeleteSessionAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_failed_delete_reports_false_rather_than_throwing(HttpStatusCode status)
    {
        // "New chat" tells the user their conversation was erased only when this is true —
        // a discarded result would claim erasure while the rows live on for 7 days.
        var (api, _) = ApiReturning(status);

        Assert.False(await api.DeleteSessionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_transport_failure_while_deleting_reports_false()
    {
        var (api, _) = ApiThrowing(new HttpRequestException("offline"));

        Assert.False(await api.DeleteSessionAsync(Guid.NewGuid()));
    }
}
