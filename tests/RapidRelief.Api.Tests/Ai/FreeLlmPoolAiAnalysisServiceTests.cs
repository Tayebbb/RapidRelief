using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.FreeLlmPool;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// The D-028 provider chain under FreeLlmPool: every failure mode yields Provider=="RuleBased"
/// and NEVER throws; a valid response yields Provider=="FreeLlmPool" with telemetry populated
/// (ModelName = response.model); the breaker only counts genuine provider attempts — a D-064
/// block (403 or content_filter) falls back WITHOUT counting. D-113: a blank
/// Ai:FreeLlmPool:BaseUrl is now the short-circuit gate (not the API key — freellmpool can
/// legitimately answer with zero key via its keyless providers). D-024 photo handling (first
/// photo as a data-URL image part, any photo problem → text-only) and end-to-end runs through
/// the REAL FreeLlmPoolClient against a fake HttpMessageHandler.
/// </summary>
public sealed class FreeLlmPoolAiAnalysisServiceTests
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

    private sealed class FakeFreeLlmPoolClient : IFreeLlmPoolClient
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
        string summary = "Warehouse fire with heavy smoke.", string finishReason = "stop",
        string model = "z-ai/glm-5.2:free")
    {
        var inner = $"{{\"predictedType\":\"{predictedType}\",\"severity\":{severity},\"summary\":{JsonSerializer.Serialize(summary)},\"confidence\":0.9}}";
        return $"{{\"model\":{JsonSerializer.Serialize(model)},\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(inner)}}},\"finish_reason\":{JsonSerializer.Serialize(finishReason)}}}],\"usage\":{{\"total_tokens\":57}}}}";
    }

    private static FreeLlmPoolAiAnalysisService Create(
        IFreeLlmPoolClient client, out AiCircuitBreaker breaker, string baseUrl = "http://localhost:8080/",
        IFileStorage? fileStorage = null, ILogger<FreeLlmPoolAiAnalysisService>? logger = null,
        TimeProvider? clock = null)
    {
        clock ??= new FixedTimeProvider(Now);
        breaker = new AiCircuitBreaker(clock, 3, TimeSpan.FromMinutes(2));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:FreeLlmPool:BaseUrl"] = baseUrl,
            ["Ai:FreeLlmPool:TextModel"] = "z-ai/glm-5.2:free",
            ["Ai:FreeLlmPool:VisionModel"] = "google/gemma-4-31b-it:free",
        }).Build();
        return new FreeLlmPoolAiAnalysisService(new RuleBasedAiAnalysisService(clock), client,
            fileStorage ?? new FakeFileStorage(), breaker, clock, config,
            logger ?? NullLogger<FreeLlmPoolAiAnalysisService>.Instance);
    }

    [Fact]
    public async Task Blank_base_url_short_circuits_to_rule_based_without_calling_the_client()
    {
        // D-113: a blank BaseUrl is the operator kill switch (replaces the old blank-ApiKey gate).
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out var breaker, baseUrl: "");

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(0, client.Calls);
        Assert.Null(outcome.ModelName);
        Assert.Null(outcome.TokensUsed);
        Assert.True(breaker.TryEnter()); // short-circuit is NOT a breaker failure
    }

    [Fact]
    public async Task Repeated_blank_base_url_calls_never_open_the_breaker()
    {
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out var breaker, baseUrl: "");

        for (var i = 0; i < 5; i++)
        {
            await service.AnalyzeWithMetadataAsync(Request());
        }

        Assert.True(breaker.TryEnter());
    }

    public static TheoryData<Exception> ProviderPathExceptions => new()
    {
        new AiProviderUnavailableException("placeholder"),
        new HttpRequestException("429 too many requests"),
        new HttpRequestException("500 internal server error"),
        new TaskCanceledException("simulated request timeout"),
    };

    [Theory]
    [MemberData(nameof(ProviderPathExceptions))]
    public async Task Client_exceptions_fall_back_to_rule_based_and_never_throw(Exception exception)
    {
        var client = new FakeFreeLlmPoolClient { Throws = exception };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(1, client.Calls);
        Assert.InRange(outcome.Assessment.PriorityScore, 0, 100);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"choices\":[]}")]
    public async Task Malformed_response_body_falls_back_to_rule_based(string body)
    {
        var client = new FakeFreeLlmPoolClient { Response = body };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Wrong_enum_value_falls_back_to_rule_based()
    {
        var client = new FakeFreeLlmPoolClient { Response = ValidBody(predictedType: "Tsunami") };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Out_of_range_severity_falls_back_to_rule_based()
    {
        var client = new FakeFreeLlmPoolClient { Response = ValidBody(severity: 7) };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task A_length_finish_reason_falls_back_to_rule_based_and_counts()
    {
        // Truncated JSON is useless — an Invalid outcome that must count (D-063).
        var client = new FakeFreeLlmPoolClient { Response = ValidBody(finishReason: "length") };
        var service = Create(client, out var breaker);

        var outcome1 = await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());
        var outcome4 = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome1.Assessment.Provider);
        Assert.Equal("RuleBased", outcome4.Assessment.Provider);
        Assert.Equal(3, client.Calls); // three Invalid results opened the breaker
        Assert.False(breaker.TryEnter());
    }

    [Fact]
    public async Task A_content_filter_finish_falls_back_without_counting_a_breaker_failure()
    {
        // D-064: a moderation verdict is a normal outcome — five in a row must leave the
        // breaker closed and keep calling the provider.
        var client = new FakeFreeLlmPoolClient { Response = ValidBody(finishReason: "content_filter") };
        var service = Create(client, out var breaker);

        for (var i = 0; i < 5; i++)
        {
            var outcome = await service.AnalyzeWithMetadataAsync(Request());
            Assert.Equal("RuleBased", outcome.Assessment.Provider);
        }

        Assert.Equal(5, client.Calls);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task A_403_block_falls_back_without_counting_a_breaker_failure()
    {
        // D-064: HTTP 403 = input moderation — same no-count rule as content_filter.
        var client = new FakeFreeLlmPoolClient { Throws = new AiProviderBlockedException("FreeLlmPool flagged the input (HTTP 403)") };
        var service = Create(client, out var breaker);

        for (var i = 0; i < 5; i++)
        {
            var outcome = await service.AnalyzeWithMetadataAsync(Request());
            Assert.Equal("RuleBased", outcome.Assessment.Provider);
        }

        Assert.Equal(5, client.Calls);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task A_block_during_the_half_open_probe_releases_the_probe_instead_of_wedging_the_breaker()
    {
        var clock = new AdvanceableTimeProvider(Now);
        var client = new FakeFreeLlmPoolClient { Throws = new AiProviderUnavailableException("down") };
        var service = Create(client, out _, clock: clock);
        for (var i = 0; i < 3; i++)
        {
            await service.AnalyzeWithMetadataAsync(Request()); // open the breaker
        }
        clock.Advance(TimeSpan.FromMinutes(3)); // breaker is half-open

        client.Throws = null;
        client.Response = ValidBody(finishReason: "content_filter");
        Assert.Equal("RuleBased", (await service.AnalyzeWithMetadataAsync(Request())).Assessment.Provider);

        client.Response = ValidBody();
        var recovered = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("FreeLlmPool", recovered.Assessment.Provider); // a NEW probe got through
    }

    [Fact]
    public async Task Valid_response_yields_freellmpool_provider_with_telemetry()
    {
        var client = new FakeFreeLlmPoolClient { Response = ValidBody(model: "nvidia/nemotron-3-super-120b-a12b:free") };
        var service = Create(client, out _);
        var request = Request(isSos: true);

        var outcome = await service.AnalyzeWithMetadataAsync(request);

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider);
        Assert.Equal(DisasterType.Fire, outcome.Assessment.PredictedType);
        Assert.Equal(Severity.Severe, outcome.Assessment.EstimatedSeverity);
        Assert.Equal("Warehouse fire with heavy smoke.", outcome.Assessment.Summary);
        Assert.Equal(request.IncidentId, outcome.Assessment.IncidentId);
        Assert.Null(outcome.Assessment.PossibleDuplicateOfId);
        // Same shared formula as the rule-based path: 20*4 + 25 + 15*(1-1/6) = 117.5 → 100.
        Assert.Equal(PriorityFormula.Compute(Severity.Severe, true, request.ReportedAtUtc, Now),
            outcome.Assessment.PriorityScore);
        // ModelName is the ACTUAL routed model from response.model, not the config echo.
        Assert.Equal("nvidia/nemotron-3-super-120b-a12b:free", outcome.ModelName);
        Assert.Equal(57, outcome.TokensUsed);
        Assert.Equal("stop", outcome.FinishReason);
        Assert.True(outcome.LatencyMs >= 0);
    }

    [Fact]
    public async Task Text_requests_carry_the_configured_text_model_and_json_object_format()
    {
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out _);

        await service.AnalyzeWithMetadataAsync(Request());

        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var root = body.RootElement;
        // D-113: a single model string, not an OpenRouter-style models[] array.
        Assert.Equal("z-ai/glm-5.2:free", root.GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("models", out _));
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("provider", out _));
        Assert.False(root.TryGetProperty("reasoning", out _));
    }

    [Fact]
    public async Task Three_failures_open_the_breaker_and_the_client_is_skipped()
    {
        var client = new FakeFreeLlmPoolClient { Throws = new AiProviderUnavailableException("down") };
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
        var client = new FakeFreeLlmPoolClient { Throws = new AiProviderUnavailableException("down") };
        var service = Create(client, out _);

        await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());
        client.Throws = null;
        client.Response = ValidBody();
        await service.AnalyzeWithMetadataAsync(Request()); // success resets
        client.Throws = new AiProviderUnavailableException("down again");
        await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal(6, client.Calls); // never opened — every call reached the client
        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_falling_back()
    {
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
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
        var client = new FakeFreeLlmPoolClient { Throws = new AiProviderUnavailableException("down") };
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

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider); // a NEW probe reached the provider
        Assert.Equal(5, client.Calls); // 3 failures + cancelled probe + successful probe
    }

    // ---- D-024 photo handling (data-URL image part, D-113 json_object on both paths) ----

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x52, 0x52, 0x2D, 0x46, 0x38];

    private const string DataUrlPrefix = "data:image/jpeg;base64,";

    [Fact]
    public async Task Readable_photo_is_sent_as_a_data_url_part_with_the_vision_timeout_and_vision_model()
    {
        var storage = new FakeFileStorage { Files = { ["photos/incident.jpg"] = JpegBytes } };
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: storage);

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/incident.jpg"]));

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider);
        Assert.True(client.LastIsVision);
        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var root = body.RootElement;
        // D-113: the configured vision model string rides in the body when a photo is attached.
        Assert.Equal("google/gemma-4-31b-it:free", root.GetProperty("model").GetString());
        var content = root.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        var url = content[1].GetProperty("image_url").GetProperty("url").GetString()!;
        Assert.StartsWith(DataUrlPrefix, url);
        Assert.Equal(JpegBytes, Convert.FromBase64String(url[DataUrlPrefix.Length..]));
        Assert.False(root.TryGetProperty("provider", out _));
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Missing_photo_file_degrades_to_text_only_and_still_analyzes()
    {
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: new FakeFileStorage());

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/not-there.jpg"]));

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider);
        Assert.False(client.LastIsVision);
        using var body = JsonDocument.Parse(client.LastRequestBody!);
        // Text-only: content stays a plain string and the text model is used.
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("messages")[1].GetProperty("content").ValueKind);
        Assert.Equal("z-ai/glm-5.2:free", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Unreadable_photo_degrades_to_text_only_without_counting_a_breaker_failure()
    {
        var storage = new FakeFileStorage { ThrowOnOpen = true };
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out var breaker, fileStorage: storage);

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/broken.png"]));

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider);
        Assert.False(client.LastIsVision);
        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public async Task Unknown_photo_extension_degrades_to_text_only()
    {
        var storage = new FakeFileStorage { Files = { ["docs/report.pdf"] = JpegBytes } };
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: storage);

        var outcome = await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["docs/report.pdf"]));

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider);
        Assert.False(client.LastIsVision);
    }

    [Theory]
    [InlineData("photos/a.jpeg", "image/jpeg")]
    [InlineData("photos/a.PNG", "image/png")]
    [InlineData("photos/a.webp", "image/webp")]
    public async Task Extension_maps_to_the_expected_mime_type_in_the_data_url(string path, string expectedMime)
    {
        var storage = new FakeFileStorage { Files = { [path] = JpegBytes } };
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var service = Create(client, out _, fileStorage: storage);

        await service.AnalyzeWithMetadataAsync(Request(photoPaths: [path]));

        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var url = body.RootElement.GetProperty("messages")[1].GetProperty("content")[1]
            .GetProperty("image_url").GetProperty("url").GetString()!;
        Assert.StartsWith($"data:{expectedMime};base64,", url);
    }

    [Fact]
    public async Task Extra_photos_are_dropped_and_only_the_first_is_sent()
    {
        var storage = new FakeFileStorage
        {
            Files = { ["photos/first.jpg"] = JpegBytes, ["photos/second.jpg"] = [0x01, 0x02] },
        };
        var client = new FakeFreeLlmPoolClient { Response = ValidBody() };
        var logger = new CapturingLogger<FreeLlmPoolAiAnalysisService>();
        var service = Create(client, out _, fileStorage: storage, logger: logger);

        await service.AnalyzeWithMetadataAsync(
            Request(photoPaths: ["photos/first.jpg", "photos/second.jpg", "photos/third.jpg"]));

        using var body = JsonDocument.Parse(client.LastRequestBody!);
        var content = body.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        var url = content[1].GetProperty("image_url").GetProperty("url").GetString()!;
        Assert.Equal(JpegBytes, Convert.FromBase64String(url[DataUrlPrefix.Length..]));
        Assert.Contains(logger.Lines, l => l.Contains('2') && l.Contains("photo", StringComparison.OrdinalIgnoreCase));
    }

    // ---- End-to-end through the REAL FreeLlmPoolClient (fake HttpMessageHandler) ----

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
            BaseAddress = new Uri("http://localhost:8080/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static FreeLlmPoolClient RealClient(HttpMessageHandler handler, string apiKey = "test-key")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:FreeLlmPool:ApiKey"] = apiKey,
            ["Ai:FreeLlmPool:TimeoutSecondsText"] = "10",
            ["Ai:FreeLlmPool:TimeoutSecondsVision"] = "20",
        }).Build();
        return new FreeLlmPoolClient(new StubHttpClientFactory(handler), config);
    }

    [Fact]
    public async Task Composite_with_real_client_returns_freellmpool_on_a_successful_http_response()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(ValidBody(), System.Text.Encoding.UTF8, "application/json"),
        }));
        var service = Create(RealClient(handler), out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider);
        Assert.Equal(57, outcome.TokensUsed);
        Assert.Equal("z-ai/glm-5.2:free", outcome.ModelName);
    }

    [Fact]
    public async Task Composite_with_real_client_falls_back_on_http_500_and_never_logs_the_api_key()
    {
        const string secretKey = "sk-live-SECRET-KEY-XYZ";
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":{\"message\":\"boom\"}}"),
        }));
        var logger = new CapturingLogger<FreeLlmPoolAiAnalysisService>();
        var service = Create(RealClient(handler, apiKey: secretKey), out _, logger: logger);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(logger.Lines, l => l.Contains(secretKey));
        Assert.Contains(logger.Lines, l => l.Contains("500"));
    }

    [Fact]
    public async Task Composite_with_real_client_treats_a_200_level_error_envelope_as_a_counted_failure()
    {
        // D-063 check 2 end-to-end: an HTTP 200 whose body is only an error envelope must count
        // against the breaker — a status-only client would record it as a success.
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"error\":{\"code\":502,\"message\":\"upstream sad\",\"metadata\":{\"error_type\":\"provider_error\"}}}",
                System.Text.Encoding.UTF8, "application/json"),
        }));
        var service = Create(RealClient(handler), out var breaker);

        for (var i = 0; i < 3; i++)
        {
            var outcome = await service.AnalyzeWithMetadataAsync(Request());
            Assert.Equal("RuleBased", outcome.Assessment.Provider);
        }

        Assert.False(breaker.TryEnter()); // three counted failures opened it
    }
}
