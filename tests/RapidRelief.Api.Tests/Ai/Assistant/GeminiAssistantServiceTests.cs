using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.Gemini;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 TEST PLAN items 1, 2 and 11 — the D-050 chain: EVERY failure mode yields
/// Provider=="Canned" with non-empty text and never throws, a safety block never counts
/// against the shared breaker, transport failures do, and no message or answer text ever
/// reaches a log line.
/// </summary>
public sealed class GeminiAssistantServiceTests
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

    private sealed class FakeGeminiClient : IGeminiClient
    {
        public int Calls;
        public Exception? Throws;
        public string? Response;
        public string? LastRequestBody;
        public bool? LastIsVision;

        public Task<string> GenerateContentAsync(string requestBody, bool isVision, CancellationToken ct = default)
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

    private static string ResponseBody(string text = "Move to higher ground now.", string finishReason = "STOP")
        => $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(text)}}}]}},"
           + $"\"finishReason\":{JsonSerializer.Serialize(finishReason)}}}],\"usageMetadata\":{{\"totalTokenCount\":91}}}}";

    private static GeminiAssistantService Create(
        IGeminiClient client, out GeminiCircuitBreaker breaker, string apiKey = "test-key",
        ILogger<GeminiAssistantService>? logger = null, TimeProvider? clock = null)
    {
        clock ??= new FixedTimeProvider(Now);
        breaker = new GeminiCircuitBreaker(clock, 3, TimeSpan.FromMinutes(2));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:ApiKey"] = apiKey,
            ["Ai:Gemini:Model"] = "gemini-3.7-flash",
        }).Build();
        return new GeminiAssistantService(client, breaker, new AssistantOptions(), config,
            logger ?? NullLogger<GeminiAssistantService>.Instance);
    }

    [Fact]
    public async Task Missing_api_key_short_circuits_to_canned_without_calling_the_client()
    {
        var client = new FakeGeminiClient { Response = ResponseBody() };
        var service = Create(client, out var breaker, apiKey: "");

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.Equal(CannedSafetyResponses.TextFor("there is flooding near my house"), answer.Text);
        Assert.Equal(0, client.Calls);
        Assert.True(breaker.TryEnter()); // a missing key is not a Gemini failure
    }

    [Fact]
    public async Task An_open_breaker_skips_the_client_and_answers_canned()
    {
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("down") };
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
        new GeminiUnavailableException("Gemini returned HTTP 429"),
        new GeminiUnavailableException("Gemini returned HTTP 503"),
        new GeminiUnavailableException("Gemini text request timed out after 10 s"),
        new HttpRequestException("connection reset"),
    };

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Client_exceptions_fall_back_to_canned_and_never_throw(Exception exception)
    {
        var client = new FakeGeminiClient { Throws = exception };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));
        Assert.Equal(1, client.Calls);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("""{"candidates":[{"finishReason":"STOP"}]}""")]
    public async Task A_structurally_broken_response_falls_back_to_canned(string body)
    {
        var client = new FakeGeminiClient { Response = body };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
    }

    [Theory]
    [InlineData("""{"candidates":[]}""")]
    [InlineData("""{"promptFeedback":{"blockReason":"SAFETY"},"candidates":[]}""")]
    public async Task A_blocked_or_empty_candidate_response_falls_back_to_canned(string body)
    {
        var client = new FakeGeminiClient { Response = body };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));
    }

    [Fact]
    public async Task A_safety_finish_reason_falls_back_to_canned()
    {
        var client = new FakeGeminiClient { Response = ResponseBody("partial", "SAFETY") };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
    }

    [Fact]
    public async Task An_answer_that_sanitises_to_nothing_falls_back_to_canned()
    {
        var client = new FakeGeminiClient { Response = ResponseBody("https://evil.example \u0000") };
        var service = Create(client, out var breaker);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Canned", answer.Provider);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task A_valid_response_is_sanitised_and_reported_as_gemini()
    {
        var client = new FakeGeminiClient
        {
            Response = ResponseBody("Move to higher ground.\u0000 See https://evil.example for maps."),
        };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Gemini", answer.Provider);
        Assert.Equal("Move to higher ground. See for maps.", answer.Text);
        Assert.False(answer.Truncated);
        Assert.Equal(91, answer.TokensUsed);
        Assert.Equal("STOP", answer.FinishReason);
        Assert.True(answer.LatencyMs >= 0);
        Assert.False(client.LastIsVision);
    }

    [Fact]
    public async Task A_truncated_response_is_still_a_gemini_answer()
    {
        var client = new FakeGeminiClient { Response = ResponseBody("Move to higher ground and", "MAX_TOKENS") };
        var service = Create(client, out _);

        var answer = await service.AskAsync(Ask());

        Assert.Equal("Gemini", answer.Provider);
        Assert.True(answer.Truncated);
    }

    [Fact]
    public async Task Five_consecutive_blocked_answers_leave_the_breaker_closed_and_keep_calling_gemini()
    {
        // D-050 anti-DoS pin: otherwise 3 hostile messages disable Gemini for EVERY user for 2 min.
        var client = new FakeGeminiClient { Response = ResponseBody("partial", "SAFETY") };
        var service = Create(client, out var breaker);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("Canned", (await service.AskAsync(Ask())).Provider);
        }

        Assert.Equal(5, client.Calls);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task Three_consecutive_transport_failures_open_the_breaker_and_stop_calling_gemini()
    {
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("Gemini returned HTTP 503") };
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
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("down") };
        var service = Create(client, out var breaker, clock: clock);
        for (var i = 0; i < 3; i++)
        {
            await service.AskAsync(Ask());
        }
        clock.Advance(TimeSpan.FromMinutes(3)); // breaker is half-open

        client.Throws = null;
        client.Response = ResponseBody("partial", "SAFETY");
        Assert.Equal("Canned", (await service.AskAsync(Ask())).Provider);

        client.Response = ResponseBody();
        var recovered = await service.AskAsync(Ask());

        Assert.Equal("Gemini", recovered.Provider);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_abandons_the_probe()
    {
        var client = new FakeGeminiClient { Response = ResponseBody() };
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
        var logger = new CapturingLogger<GeminiAssistantService>();
        var client = new FakeGeminiClient { Response = ResponseBody($"Move away. {answerMarker}") };
        var service = Create(client, out _, logger: logger);

        await service.AskAsync(Ask($"there is a fire and {questionMarker}"));
        client.Throws = new GeminiUnavailableException("Gemini returned HTTP 500");
        await service.AskAsync(Ask($"there is a fire and {questionMarker}"));

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(logger.Lines, line => line.Contains(questionMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Lines, line => line.Contains(answerMarker, StringComparison.Ordinal));
    }
}
