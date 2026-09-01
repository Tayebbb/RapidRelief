using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using RapidRelief.Api.Features.Ai.Gemini;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 chunk 2 — real Gemini transport against a fake HttpMessageHandler: POST to the pinned
/// model route, API key travels as the x-goog-api-key header (never in the URL), D-026
/// linked-CTS timeouts (text vs vision), ZERO retries, and every failure mode surfaces as
/// GeminiUnavailableException with metadata only (status code, no response body, no key).
/// </summary>
public sealed class GeminiClientTests
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
            // Mirrors the AiModule "gemini" named-client registration: pinned base address,
            // infinite HttpClient timeout (D-026 timeouts are per-request linked CTS).
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }
    }

    private static GeminiClient Create(HttpMessageHandler handler, out StubHttpClientFactory factory,
        int textTimeoutSeconds = 10, int visionTimeoutSeconds = 20)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:ApiKey"] = ApiKey,
            ["Ai:Gemini:Model"] = "gemini-3.7-flash",
            ["Ai:Gemini:TimeoutSecondsText"] = textTimeoutSeconds.ToString(),
            ["Ai:Gemini:TimeoutSecondsVision"] = visionTimeoutSeconds.ToString(),
        }).Build();
        factory = new StubHttpClientFactory(handler);
        return new GeminiClient(factory, config);
    }

    private static HttpResponseMessage Ok(string body = "{\"candidates\":[]}")
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Success_posts_the_body_to_the_model_route_with_the_key_as_a_header_only()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok("{\"candidates\":[{\"x\":1}]}")));
        var client = Create(handler, out var factory);

        var response = await client.GenerateContentAsync("{\"payload\":true}", isVision: false);

        Assert.Equal("{\"candidates\":[{\"x\":1}]}", response);
        Assert.Equal("gemini", factory.RequestedName);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.7-flash:generateContent",
            request.RequestUri!.ToString());
        Assert.Equal(ApiKey, Assert.Single(request.Headers.GetValues("x-goog-api-key")));
        Assert.DoesNotContain(ApiKey, request.RequestUri.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"payload\":true}", handler.LastBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "400")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    public async Task Non_2xx_throws_unavailable_with_the_status_code_and_without_the_body(
        HttpStatusCode status, string expectedInMessage)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"error\":\"sensitive-detail-must-not-leak\"}"),
        }));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<GeminiUnavailableException>(
            () => client.GenerateContentAsync("{}", isVision: false));

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

        var ex = await Assert.ThrowsAsync<GeminiUnavailableException>(
            () => client.GenerateContentAsync("{}", isVision: false));

        Assert.Contains("429", ex.Message);
        Assert.Single(handler.Requests); // D-026: zero retries — fallback is instant and free
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

        var ex = await Assert.ThrowsAsync<GeminiUnavailableException>(
            () => client.GenerateContentAsync("{}", isVision: false));

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

        var response = await client.GenerateContentAsync("{}", isVision: true);

        Assert.Equal("{\"candidates\":[]}", response);
    }

    [Fact]
    public async Task Network_errors_are_wrapped_in_unavailable_without_leaking_the_key()
    {
        using var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));
        var client = Create(handler, out _);

        var ex = await Assert.ThrowsAsync<GeminiUnavailableException>(
            () => client.GenerateContentAsync("{}", isVision: false));

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
            () => client.GenerateContentAsync("{}", isVision: false, cts.Token));
    }
}
