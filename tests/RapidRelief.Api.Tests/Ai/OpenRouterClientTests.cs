using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.OpenRouter;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// OpenRouter transport (D-060/D-063/D-064) against a fake HttpMessageHandler: POST to the
/// pinned chat-completions route, API key travels as a per-request Authorization: Bearer
/// header (never in the URL) plus X-Title attribution, D-026 linked-CTS timeouts (text vs
/// vision), ZERO retries, and the three-way classification — non-2xx except 403 →
/// AiProviderUnavailableException with the body never read into any message; 403 →
/// AiProviderBlockedException on the status alone; 2xx with a top-level error and no choices
/// → Unavailable reading ONLY error.code + sanitized error.metadata.error_type (error.message
/// never); finish_reason "error" → Unavailable.
/// </summary>
public sealed class OpenRouterClientTests
{
    private const string ApiKey = "sk-test-secret-123";

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> onSend)
        : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(CancellationToken.None);
            return await onSend(request, ct);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public string? RequestedName;

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            // Mirrors the AiModule "openrouter" named-client registration: pinned base address,
            // infinite HttpClient timeout (D-026 timeouts are per-request linked CTS).
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://openrouter.ai/"),
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }
    }

    private static OpenRouterClient Create(HttpMessageHandler handler, out StubHttpClientFactory factory,
        int textTimeoutSeconds = 10, int visionTimeoutSeconds = 20)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:OpenRouter:ApiKey"] = ApiKey,
            ["Ai:OpenRouter:TimeoutSecondsText"] = textTimeoutSeconds.ToString(),
            ["Ai:OpenRouter:TimeoutSecondsVision"] = visionTimeoutSeconds.ToString(),
        }).Build();
        factory = new StubHttpClientFactory(handler);
        return new OpenRouterClient(factory, config);
    }

    private static HttpResponseMessage Ok(string body = "{\"choices\":[]}")
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Success_posts_the_body_to_the_chat_completions_route_with_bearer_and_attribution_headers()
    {
        const string responseBody = "{\"choices\":[{\"message\":{\"content\":\"x\"},\"finish_reason\":\"stop\"}]}";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(responseBody)));
        var client = Create(handler, out var factory);

        var response = await client.SendAsync("{\"payload\":true}", isVision: false);

        Assert.Equal(responseBody, response);
        Assert.Equal("openrouter", factory.RequestedName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal($"Bearer {ApiKey}", Assert.Single(request.Headers.GetValues("Authorization")));
        Assert.Equal("RapidRelief", Assert.Single(request.Headers.GetValues("X-Title")));
        Assert.DoesNotContain(ApiKey, request.RequestUri.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"payload\":true}", handler.LastBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "400")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.PaymentRequired, "402")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    public async Task Non_2xx_throws_unavailable_with_the_status_code_and_without_the_body(
        HttpStatusCode status, string expectedInMessage)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"error\":{\"message\":\"sensitive-detail-must-not-leak\"}}"),
        }));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains(expectedInMessage, ex.Message);
        Assert.DoesNotContain("sensitive-detail-must-not-leak", ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message);
    }

    [Fact]
    public async Task A_429_is_a_single_counted_failure_with_zero_retries()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains("429", ex.Message);
        Assert.Single(handler.Requests); // D-026/D-060: zero retries — fallback is instant and free
    }

    [Fact]
    public async Task A_403_throws_blocked_on_the_status_alone_without_reading_the_body()
    {
        // D-064: OpenRouter signals input moderation with HTTP 403 — the status suffices.
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"message\":\"user text echoed here must not leak\"}}"),
        }));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderBlockedException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains("403", ex.Message);
        Assert.DoesNotContain("user text echoed here must not leak", ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message);
        Assert.Single(handler.Requests); // no retry on a block either
    }

    [Fact]
    public async Task A_200_with_a_top_level_error_and_no_choices_throws_unavailable_with_code_and_type_only()
    {
        // D-063 check 2: a routed-provider failure can come back as HTTP 200 + error envelope.
        // Only error.code and the sanitized error.metadata.error_type may enter the message —
        // error.message can echo user content and must never be read.
        const string body = "{\"error\":{\"code\":502,\"message\":\"user-street-address-echo must never leak\","
            + "\"metadata\":{\"error_type\":\"provider_error\",\"raw\":\"also secret\"}}}";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(body)));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains("502", ex.Message);
        Assert.Contains("provider_error", ex.Message);
        Assert.DoesNotContain("user-street-address-echo", ex.Message);
        Assert.DoesNotContain("must never leak", ex.Message);
        Assert.DoesNotContain("also secret", ex.Message);
    }

    [Fact]
    public async Task A_200_error_without_metadata_still_reports_the_code()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Ok("{\"error\":{\"code\":429,\"message\":\"rate limited\"}}")));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains("429", ex.Message);
        Assert.DoesNotContain("rate limited", ex.Message);
    }

    [Fact]
    public async Task A_hostile_error_type_is_sanitized_before_it_reaches_the_exception_message()
    {
        const string body = "{\"error\":{\"code\":500,\"metadata\":{\"error_type\":\"evil\\r\\n[FAKE] <script>alert(1)</script>AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}}}";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(body)));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.DoesNotContain("\n", ex.Message);
        Assert.DoesNotContain("[", ex.Message);
        Assert.DoesNotContain("<", ex.Message);
        // [A-Za-z0-9_] only, clamped to 32.
        Assert.Contains("evilFAKEscriptalert1scriptAAAAAA", ex.Message);
    }

    [Fact]
    public async Task A_200_with_finish_reason_error_throws_unavailable()
    {
        // D-063 check 3: a provider that failed mid-generation still returns choices.
        const string body = "{\"choices\":[{\"message\":{\"content\":\"partial\"},\"finish_reason\":\"error\"}]}";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(body)));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains("mid-generation", ex.Message);
    }

    [Fact]
    public async Task A_200_with_both_error_and_choices_is_returned_verbatim_for_the_parsers()
    {
        // Only "error AND no choices" is the client's business; anything else is parser turf.
        const string body = "{\"error\":{\"code\":1},\"choices\":[{\"message\":{\"content\":\"x\"},\"finish_reason\":\"stop\"}]}";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(body)));
        var client = Create(handler, out _);

        Assert.Equal(body, await client.SendAsync("{}", isVision: false));
    }

    [Fact]
    public async Task A_200_with_a_non_object_first_choice_is_returned_verbatim_for_the_parsers()
    {
        // A garbage element must land in the parsers' classified Invalid bucket, not throw here.
        const string body = "{\"choices\":[123]}";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(body)));
        var client = Create(handler, out _);

        Assert.Equal(body, await client.SendAsync("{}", isVision: false));
    }

    [Fact]
    public async Task A_huge_numeric_error_code_is_clamped_before_it_reaches_the_exception_message()
    {
        var body = $"{{\"error\":{{\"code\":{new string('9', 500)},\"message\":\"never read\"}}}}";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(body)));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains(new string('9', 32), ex.Message);
        Assert.DoesNotContain(new string('9', 33), ex.Message);
    }

    [Fact]
    public async Task An_unparseable_2xx_body_is_returned_verbatim_so_the_parsers_reject_it()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok("this is not json")));
        var client = Create(handler, out _);

        Assert.Equal("this is not json", await client.SendAsync("{}", isVision: false));
    }

    [Fact]
    public async Task Text_timeout_cancels_the_request_and_throws_unavailable()
    {
        using var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok();
        });
        var client = Create(handler, out _, textTimeoutSeconds: 1);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.Contains("timed out", ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message);
    }

    [Fact]
    public async Task Vision_requests_use_the_longer_vision_timeout()
    {
        // Delay (2 s) sits between the text timeout (1 s) and the vision timeout (8 s):
        // the same handler times out as text but succeeds as vision.
        using var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return Ok();
        });
        var client = Create(handler, out _, textTimeoutSeconds: 1, visionTimeoutSeconds: 8);

        var response = await client.SendAsync("{}", isVision: true);

        Assert.Equal("{\"choices\":[]}", response);
    }

    [Fact]
    public async Task Network_errors_are_wrapped_in_unavailable_without_leaking_the_key()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.SendAsync("{}", isVision: false));

        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.DoesNotContain(ApiKey, ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_as_cancellation_not_unavailable()
    {
        using var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok();
        });
        var client = Create(handler, out _);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync("{}", isVision: false, cts.Token));
    }
}
