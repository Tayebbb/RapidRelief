using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.OpenRouter;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// The D-050 chain under OpenRouter: EVERY failure mode yields Provider=="Canned" with
/// non-empty text and never throws, a block (HTTP 403 or finish_reason content_filter) never
/// counts against the shared breaker, transport/structural failures do, and no message or
/// answer text ever reaches a log line.
/// </summary>
public sealed class OpenRouterAssistantServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdvanceableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeOpenRouterClient : IOpenRouterClient
    {
        public int Calls;
        public Exception? Throws;
        public string? Response;
        public string? LastRequestBody;
        public bool? LastIsVision;

        public Task<string> SendAsync(string requestBody, bool isVision, CancellationToken ct = default)
        {
            Calls++;
            LastRequestBody = requestBody;
            LastIsVision = isVision;
            ct.ThrowIfCancellationRequested();
            if (Throws is not null)
            {
                throw Throws;
            }
            return Task.FromResult(Response!);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<string> Lines = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add(formatter(state, exception) + (exception is null ? "" : $" | {exception}"));
            }
        }
    }

    private static AssistantAsk Ask(string question = "there is flooding near my house")
        => new(question, Array.Empty<AssistantTurn>(), AssistantContext.None);

    private static string ResponseBody(string text = "Move to higher ground now.", string finishReason = "stop")
        => $"{{\"model\":\"z-ai/glm-5.2:free\",\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}}},"
           + $"\"finish_reason\":{JsonSerializer.Serialize(finishReason)}}}],\"usage\":{{\"total_tokens\":91}}}}";

    private static OpenRouterAssistantService Create(
        IOpenRouterClient client, out AiCircuitBreaker breaker, string apiKey = "test-key",
        ILogger<OpenRouterAssistantService>? logger = null, TimeProvider? clock = null)
    {
        clock ??= new FixedTimeProvider(Now);
        breaker = new AiCircuitBreaker(clock, 3, TimeSpan.FromMinutes(2));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:OpenRouter:ApiKey"] = apiKey,
            ["Ai:OpenRouter:TextModel"] = "z-ai/glm-5.2:free",
            ["Ai:OpenRouter:TextFallbackModel"] = "nvidia/nemotron-3-super-120b-a12b:free",
        }).Build();
        return new OpenRouterAssistantService(client, breaker, new AssistantOptions(), config,
            logger ?? NullLogger<OpenRouterAssistantService>.Instance);
    }

    [Fact]
    public async Task Missing_api_key_short_circuits_to_canned_without_calling_the_client()
    {
        var client = new FakeOpenRouterClient { Response = ResponseBody() };
        var service = Create(client, out var breaker, apiKey: "");

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.Equal(CannedSafetyResponses.TextFor("there is flooding near my house"), answer.Text);
        Assert.Equal(0, client.Calls);
        Assert.True(breaker.TryEnter()); // a missing key is not a provider failure
    }

    [Fact]
    public async Task An_open_breaker_skips_the_client_and_answers_canned()
    {
        var client = new FakeOpenRouterClient { Throws = new AiProviderUnavailableException("down") };
        var service = Create(client, out var breaker);
        for (var i = 0; i < 3; i++)
        {
            await service.AskAsync(Ask());
        }
        var callsWhileClosed = client.Calls;

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.Equal(callsWhileClosed, client.Calls);
        Assert.False(breaker.TryEnter());
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new AiProviderUnavailableException("OpenRouter returned HTTP 429"),
        new AiProviderUnavailableException("OpenRouter returned HTTP 503"),
        new AiProviderUnavailableException("OpenRouter text request timed out after 10 s"),
        new HttpRequestException("connection reset"),
    };

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Client_exceptions_fall_back_to_canned_and_never_throw(Exception exception)
    {
        var client = new FakeOpenRouterClient { Throws = exception };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));
        Assert.Equal(1, client.Calls);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("""{"choices":[{"finish_reason":"stop"}]}""")]
    public async Task A_structurally_broken_response_falls_back_to_canned(string body)
    {
        var client = new FakeOpenRouterClient { Response = body };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
    }

    [Fact]
    public async Task A_choiceless_response_is_a_counted_failure_that_still_answers_canned()
    {
        // A bare 200 with no choices (and no error envelope — the client already threw on that)
        // is a proxy/quota failure: it must count, or it would keep re-arming probes forever.
        var client = new FakeOpenRouterClient { Response = """{"choices":[]}""" };
        var service = Create(client, out var breaker);

        for (var i = 0; i < 3; i++)
        {
            var answer = await service.AskAsync(Ask());
            Assert.Equal("Canned", answer.Provider);
            Assert.False(string.IsNullOrWhiteSpace(answer.Text));
        }

        Assert.False(breaker.TryEnter()); // three Invalid results opened the shared breaker
    }

    [Fact]
    public async Task A_content_filter_finish_reason_falls_back_to_canned()
    {
        var client = new FakeOpenRouterClient { Response = ResponseBody("partial", "content_filter") };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
    }

    [Fact]
    public async Task A_403_block_falls_back_to_canned_without_counting_a_breaker_failure()
    {
        // D-064: OpenRouter input moderation is HTTP 403 — canned outcome, breaker untouched.
        var client = new FakeOpenRouterClient { Throws = new AiProviderBlockedException("OpenRouter flagged the input (HTTP 403)") };
        var service = Create(client, out var breaker);

        for (var i = 0; i < 5; i++)
        {
            var answer = await service.AskAsync(Ask());
            Assert.Equal("Canned", answer.Provider);
            Assert.False(string.IsNullOrWhiteSpace(answer.Text));
        }

        Assert.Equal(5, client.Calls);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task An_answer_that_sanitises_to_nothing_falls_back_to_canned()
    {
        var client = new FakeOpenRouterClient { Response = ResponseBody("https://evil.example \u0000") };
        var service = Create(client, out var breaker);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task A_valid_response_is_sanitised_and_reported_as_openrouter()
    {
        var client = new FakeOpenRouterClient
        {
            Response = ResponseBody("Move to higher ground.\u0000 See https://evil.example for maps."),
        };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("OpenRouter", answer.Provider);
        Assert.Equal("Move to higher ground. See for maps.", answer.Text);
        Assert.False(answer.Truncated);
        Assert.Equal(91, answer.TokensUsed);
        Assert.Equal("stop", answer.FinishReason);
        Assert.True(answer.LatencyMs >= 0);
        Assert.False(client.LastIsVision);
    }

    [Fact]
    public async Task A_truncated_response_is_still_a_live_answer()
    {
        var client = new FakeOpenRouterClient { Response = ResponseBody("Move to higher ground and", "length") };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("OpenRouter", answer.Provider);
        Assert.True(answer.Truncated);
    }

    [Fact]
    public async Task The_request_carries_the_text_model_pair_with_reasoning_disabled()
    {
        var client = new FakeOpenRouterClient { Response = ResponseBody() };
        var service = Create(client, out _);

        await service.AskAsync(Ask());

        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var root = body.RootElement;
        Assert.Equal(
            new[] { "z-ai/glm-5.2:free", "nvidia/nemotron-3-super-120b-a12b:free" },
            root.GetProperty("models").EnumerateArray().Select(m => m.GetString()).ToArray());
        Assert.False(root.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.False(root.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task Five_consecutive_blocked_answers_leave_the_breaker_closed_and_keep_calling_the_provider()
    {
        // D-050/D-064 anti-DoS pin: otherwise 3 hostile messages disable AI for EVERY user for 2 min.
        var client = new FakeOpenRouterClient { Response = ResponseBody("partial", "content_filter") };
        var service = Create(client, out var breaker);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("Canned", (await service.AskAsync(Ask())).Provider);
        }

        Assert.Equal(5, client.Calls);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task Three_consecutive_transport_failures_open_the_breaker_and_stop_calling_the_provider()
    {
        var client = new FakeOpenRouterClient { Throws = new AiProviderUnavailableException("OpenRouter returned HTTP 503") };
        var service = Create(client, out var breaker);

        for (var i = 0; i < 3; i++)
        {
            await service.AskAsync(Ask());
        }
        await service.AskAsync(Ask());

        Assert.Equal(3, client.Calls);
        Assert.False(breaker.TryEnter());
    }

    [Fact]
    public async Task A_blocked_answer_releases_the_half_open_probe_instead_of_wedging_the_breaker()
    {
        var clock = new AdvanceableTimeProvider(Now);
        var client = new FakeOpenRouterClient { Throws = new AiProviderUnavailableException("down") };
        var service = Create(client, out var breaker, clock: clock);
        for (var i = 0; i < 3; i++)
        {
            await service.AskAsync(Ask());
        }
        clock.Advance(TimeSpan.FromMinutes(3)); // breaker is half-open

        client.Throws = null;
        client.Response = ResponseBody("partial", "content_filter");
        Assert.Equal("Canned", (await service.AskAsync(Ask())).Provider);

        client.Response = ResponseBody();
        var recovered = await service.AskAsync(Ask());

        Assert.Equal("OpenRouter", recovered.Provider);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_abandons_the_probe()
    {
        var client = new FakeOpenRouterClient { Response = ResponseBody() };
        var service = Create(client, out var breaker);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AskAsync(Ask(), cts.Token));

        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task Neither_the_question_nor_the_answer_ever_reaches_a_log_line()
    {
        const string questionMarker = "my-house-is-at-42-Marker-Road";
        const string answerMarker = "ANSWER-MARKER-4711";
        var logger = new CapturingLogger<OpenRouterAssistantService>();
        var client = new FakeOpenRouterClient { Response = ResponseBody($"Move away. {answerMarker}") };
        var service = Create(client, out _, logger: logger);

        await service.AskAsync(Ask($"there is a fire and {questionMarker}"));
        client.Throws = new AiProviderUnavailableException("OpenRouter returned HTTP 500");
        await service.AskAsync(Ask($"there is a fire and {questionMarker}"));

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(logger.Lines, line => line.Contains(questionMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Lines, line => line.Contains(answerMarker, StringComparison.Ordinal));
    }
}
