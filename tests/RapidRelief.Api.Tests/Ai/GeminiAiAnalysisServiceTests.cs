using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Gemini;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN item 2 — the D-028 provider chain: every failure mode yields
/// Provider=="RuleBased" and NEVER throws; a valid response yields Provider=="Gemini"
/// with telemetry populated; the breaker only counts genuine Gemini attempts. Chunk 2 adds
/// D-024 photo handling (first photo inline, any photo problem → text-only) and end-to-end
/// runs through the REAL GeminiClient against a fake HttpMessageHandler.
/// </summary>
public sealed class GeminiAiAnalysisServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

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

    private sealed class FakeFileStorage : IFileStorage
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public bool ThrowOnOpen;

        public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
        {
            if (ThrowOnOpen)
            {
                throw new IOException("simulated disk failure");
            }
            return Task.FromResult<Stream?>(Files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);
        }

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Collects formatted log output so tests can assert the API key never appears.</summary>
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

    private static AiAnalysisRequest Request(bool isSos = false, IReadOnlyList<string>? photoPaths = null)
        => new(Guid.NewGuid(), DisasterType.Flood, "Street knee-deep in water",
            new GeoPoint(23.8103, 90.4125), isSos, Now.AddHours(-1), photoPaths ?? Array.Empty<string>());

    private static string ValidBody(string predictedType = "Fire", int severity = 4,
        string summary = "Warehouse fire with heavy smoke.", string finishReason = "STOP")
    {
        var inner = $"{{\"predictedType\":\"{predictedType}\",\"severity\":{severity},\"summary\":{JsonSerializer.Serialize(summary)},\"confidence\":0.9}}";
        return $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(inner)}}}]}},\"finishReason\":{JsonSerializer.Serialize(finishReason)}}}],\"usageMetadata\":{{\"totalTokenCount\":57}}}}";
    }

    private static GeminiAiAnalysisService Create(
        IGeminiClient client, out GeminiCircuitBreaker breaker, string apiKey = "test-key",
        IFileStorage? fileStorage = null, ILogger<GeminiAiAnalysisService>? logger = null,
        TimeProvider? clock = null)
    {
        clock ??= new FixedTimeProvider(Now);
        breaker = new GeminiCircuitBreaker(clock, 3, TimeSpan.FromMinutes(2));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:ApiKey"] = apiKey,
            ["Ai:Gemini:Model"] = "gemini-3.7-flash",
        }).Build();
        return new GeminiAiAnalysisService(new RuleBasedAiAnalysisService(clock), client,
            fileStorage ?? new FakeFileStorage(), breaker, clock, config,
            logger ?? NullLogger<GeminiAiAnalysisService>.Instance);
    }

    [Fact]
    public async Task Missing_api_key_short_circuits_to_rule_based_without_calling_the_client()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out var breaker, apiKey: "");

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(0, client.Calls);
        Assert.Null(outcome.ModelName);
        Assert.Null(outcome.TokensUsed);
        Assert.True(breaker.TryEnter()); // short-circuit is NOT a breaker failure
    }

    [Fact]
    public async Task Repeated_missing_key_calls_never_open_the_breaker()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out var breaker, apiKey: "");

        for (var i = 0; i < 5; i++)
        {
            await service.AnalyzeWithMetadataAsync(Request());
        }

        Assert.True(breaker.TryEnter());
    }

    public static TheoryData<Exception> GeminiPathExceptions => new()
    {
        new GeminiUnavailableException("placeholder"),
        new HttpRequestException("429 too many requests"),
        new HttpRequestException("500 internal server error"),
        new TaskCanceledException("simulated request timeout"),
    };

    [Theory]
    [MemberData(nameof(GeminiPathExceptions))]
    public async Task Client_exceptions_fall_back_to_rule_based_and_never_throw(Exception exception)
    {
        var client = new FakeGeminiClient { Throws = exception };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(1, client.Calls);
        Assert.InRange(outcome.Assessment.PriorityScore, 0, 100);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"candidates\":[]}")]
    public async Task Malformed_response_body_falls_back_to_rule_based(string body)
    {
        var client = new FakeGeminiClient { Response = body };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Wrong_enum_value_falls_back_to_rule_based()
    {
        var client = new FakeGeminiClient { Response = ValidBody(predictedType: "Tsunami") };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Out_of_range_severity_falls_back_to_rule_based()
    {
        var client = new FakeGeminiClient { Response = ValidBody(severity: 7) };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Max_tokens_finish_reason_falls_back_to_rule_based()
    {
        var client = new FakeGeminiClient { Response = ValidBody(finishReason: "MAX_TOKENS") };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Valid_response_yields_gemini_provider_with_telemetry()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _);
        var request = Request(isSos: true);

        var outcome = await service.AnalyzeWithMetadataAsync(request);

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.Equal(DisasterType.Fire, outcome.Assessment.PredictedType);
        Assert.Equal(Severity.Severe, outcome.Assessment.EstimatedSeverity);
        Assert.Equal("Warehouse fire with heavy smoke.", outcome.Assessment.Summary);
        Assert.Equal(request.IncidentId, outcome.Assessment.IncidentId);
        Assert.Null(outcome.Assessment.PossibleDuplicateOfId);
        // Same shared formula as the rule-based path: 20*4 + 25 + 15*(1-1/6) = 117.5 → 100.
        Assert.Equal(PriorityFormula.Compute(Severity.Severe, true, request.ReportedAtUtc, Now),
            outcome.Assessment.PriorityScore);
        Assert.Equal("gemini-3.7-flash", outcome.ModelName);
        Assert.Equal(57, outcome.TokensUsed);
        Assert.Equal("STOP", outcome.FinishReason);
        Assert.True(outcome.LatencyMs >= 0);
    }

    [Fact]
    public async Task Three_failures_open_the_breaker_and_the_client_is_skipped()
    {
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("down") };
        var service = Create(client, out _);

        for (var i = 0; i < 3; i++)
        {
            await service.AnalyzeWithMetadataAsync(Request());
        }
        Assert.Equal(3, client.Calls);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(3, client.Calls); // breaker open — no fourth attempt
    }

    [Fact]
    public async Task Success_between_failures_keeps_the_breaker_closed()
    {
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("down") };
        var service = Create(client, out _);

        await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());
        client.Throws = null;
        client.Response = ValidBody();
        await service.AnalyzeWithMetadataAsync(Request()); // success resets
        client.Throws = new GeminiUnavailableException("down again");
        await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal(6, client.Calls); // never opened — every call reached the client
        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_falling_back()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AnalyzeWithMetadataAsync(Request(), cts.Token));
    }

    [Fact]
    public async Task Cancellation_during_the_half_open_probe_does_not_wedge_the_breaker()
    {
        var clock = new AdvanceableTimeProvider(Now);
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("down") };
        var service = Create(client, out _, clock: clock);
        for (var i = 0; i < 3; i++)
        {
            await service.AnalyzeWithMetadataAsync(Request()); // open the breaker
        }
        clock.Advance(TimeSpan.FromMinutes(2)); // half-open window reached

        // The probe holder gets cancelled between TryEnter and Record* — the OCE path.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AnalyzeWithMetadataAsync(Request(), cts.Token));

        client.Throws = null;
        client.Response = ValidBody();
        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("Gemini", outcome.Assessment.Provider); // a NEW probe reached Gemini
        Assert.Equal(5, client.Calls); // 3 failures + cancelled probe + successful probe
    }

    // ---- D-024 photo handling (chunk 2) ----

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x52, 0x52, 0x2D, 0x46, 0x38];

    [Fact]
    public async Task Readable_photo_is_sent_inline_with_the_vision_timeout()
    {
        var storage = new FakeFileStorage { Files = { ["photos/incident.jpg"] = JpegBytes } };
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: storage);

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/incident.jpg"]));

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.True(client.LastIsVision);
        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal(2, parts.GetArrayLength());
        var inline = parts[1].GetProperty("inlineData");
        Assert.Equal("image/jpeg", inline.GetProperty("mimeType").GetString());
        Assert.Equal(JpegBytes, Convert.FromBase64String(inline.GetProperty("data").GetString()!));
    }

    [Fact]
    public async Task Missing_photo_file_degrades_to_text_only_and_still_analyzes()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: new FakeFileStorage());

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/not-there.jpg"]));

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.False(client.LastIsVision);
        using var body = JsonDocument.Parse(client.LastRequestBody!);
        Assert.Equal(1, body.RootElement.GetProperty("contents")[0].GetProperty("parts").GetArrayLength());
    }

    [Fact]
    public async Task Unreadable_photo_degrades_to_text_only_without_counting_a_breaker_failure()
    {
        var storage = new FakeFileStorage { ThrowOnOpen = true };
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out var breaker, fileStorage: storage);

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/broken.png"]));

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.False(client.LastIsVision);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task Unknown_photo_extension_degrades_to_text_only()
    {
        var storage = new FakeFileStorage { Files = { ["docs/report.pdf"] = JpegBytes } };
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: storage);

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["docs/report.pdf"]));

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.False(client.LastIsVision);
    }

    [Theory]
    [InlineData("photos/a.jpeg", "image/jpeg")]
    [InlineData("photos/a.PNG", "image/png")]
    [InlineData("photos/a.webp", "image/webp")]
    public async Task Extension_maps_to_the_expected_mime_type(string path, string expectedMime)
    {
        var storage = new FakeFileStorage { Files = { [path] = JpegBytes } };
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: storage);

        await service.AnalyzeWithMetadataAsync(Request(photoPaths: [path]));

        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var inline = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData");
        Assert.Equal(expectedMime, inline.GetProperty("mimeType").GetString());
    }

    [Fact]
    public async Task Extra_photos_are_dropped_and_only_the_first_is_sent()
    {
        var storage = new FakeFileStorage
        {
            Files = { ["photos/first.jpg"] = JpegBytes, ["photos/second.jpg"] = [0x01, 0x02] },
        };
        var client = new FakeGeminiClient { Response = ValidBody() };
        var logger = new CapturingLogger<GeminiAiAnalysisService>();
        var service = Create(client, out _, fileStorage: storage, logger: logger);

        await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/first.jpg", "photos/second.jpg", "photos/third.jpg"]));

        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal(2, parts.GetArrayLength());
        Assert.Equal(JpegBytes,
            Convert.FromBase64String(parts[1].GetProperty("inlineData").GetProperty("data").GetString()!));
        Assert.Contains(logger.Lines, l => l.Contains('2') && l.Contains("photo", StringComparison.OrdinalIgnoreCase));
    }

    // ---- End-to-end through the REAL GeminiClient (fake HttpMessageHandler) ----

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> onSend)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => onSend(request, ct);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static GeminiClient RealClient(HttpMessageHandler handler, string apiKey = "test-key")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:ApiKey"] = apiKey,
            ["Ai:Gemini:Model"] = "gemini-3.7-flash",
            ["Ai:Gemini:TimeoutSecondsText"] = "10",
            ["Ai:Gemini:TimeoutSecondsVision"] = "20",
        }).Build();
        return new GeminiClient(new StubHttpClientFactory(handler), config);
    }

    [Fact]
    public async Task Composite_with_real_client_returns_gemini_on_a_successful_http_response()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json"),
        }));
        var service = Create(RealClient(handler), out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.Equal(57, outcome.TokensUsed);
        Assert.Equal("gemini-3.7-flash", outcome.ModelName);
    }

    [Fact]
    public async Task Composite_with_real_client_falls_back_on_http_500_and_never_logs_the_api_key()
    {
        const string secretKey = "sk-live-SECRET-KEY-XYZ";
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":{\"message\":\"boom\"}}"),
        }));
        var logger = new CapturingLogger<GeminiAiAnalysisService>();
        var service = Create(RealClient(handler, secretKey), out _, apiKey: secretKey, logger: logger);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(logger.Lines, l => l.Contains(secretKey));
        Assert.Contains(logger.Lines, l => l.Contains("500"));
    }
}
