using System.Diagnostics;
using RapidRelief.Api.Features.Ai.Gemini;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai;

/// <summary>Feature-local analysis result: the contract DTO plus persistence-only telemetry.</summary>
internal sealed record AiAnalysisOutcome(
    AiAssessmentDto Assessment,
    string? ModelName,
    int LatencyMs,
    int? TokensUsed,
    string? FinishReason);

/// <summary>
/// D-028 composite provider chain: empty Ai:Gemini:ApiKey → straight to rule-based;
/// breaker open → rule-based; otherwise try Gemini and fall back on ANY failure while
/// counting it against the breaker. Never throws for analysis failures; logs metadata
/// only (exception type, latency, model — never description/photo/response text).
/// </summary>
internal sealed class GeminiAiAnalysisService : IAiAnalysisService
{
    private readonly RuleBasedAiAnalysisService _fallback;
    private readonly IGeminiClient _client;
    private readonly GeminiCircuitBreaker _breaker;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiAiAnalysisService> _logger;

    public GeminiAiAnalysisService(
        RuleBasedAiAnalysisService fallback,
        IGeminiClient client,
        GeminiCircuitBreaker breaker,
        TimeProvider timeProvider,
        IConfiguration config,
        ILogger<GeminiAiAnalysisService> logger)
    {
        _fallback = fallback;
        _client = client;
        _breaker = breaker;
        _timeProvider = timeProvider;
        _config = config;
        _logger = logger;
    }

    public async Task<AiAssessmentDto> AnalyzeIncidentAsync(AiAnalysisRequest request, CancellationToken ct = default)
        => (await AnalyzeWithMetadataAsync(request, ct)).Assessment;

    /// <summary>Rich variant used by the pipeline worker to fill the telemetry columns.</summary>
    internal async Task<AiAnalysisOutcome> AnalyzeWithMetadataAsync(AiAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var apiKey = _config["Ai:Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // D-028: missing key never crashes and never counts against the breaker.
            return await FallbackAsync(request, stopwatch, ct);
        }

        if (!_breaker.TryEnter())
        {
            _logger.LogInformation("Gemini breaker open — rule-based fallback for incident {IncidentId}",
                request.IncidentId);
            return await FallbackAsync(request, stopwatch, ct);
        }

        var model = _config["Ai:Gemini:Model"] ?? "gemini-3.7-flash";
        try
        {
            var responseBody = await _client.GenerateContentAsync(request, ct);

            if (!GeminiResponseParser.TryParse(responseBody, out var parsed, out var rejectReason))
            {
                throw new GeminiUnavailableException($"Response rejected: {rejectReason}");
            }

            stopwatch.Stop();
            _breaker.RecordSuccess();

            var severity = (Severity)parsed!.Severity;
            var priority = PriorityFormula.Compute(severity, request.IsSos, request.ReportedAtUtc,
                _timeProvider.GetUtcNow());
            // Metadata only — never description/photo/response text (blueprint PII rule).
            _logger.LogInformation(
                "Gemini assessed incident {IncidentId}: model {Model}, {LatencyMs} ms, {Tokens} tokens, confidence {Confidence:F2}",
                request.IncidentId, model, stopwatch.ElapsedMilliseconds, parsed.TotalTokenCount, parsed.Confidence);

            var dto = new AiAssessmentDto(request.IncidentId, parsed.PredictedType, severity,
                priority, parsed.Summary, PossibleDuplicateOfId: null, Provider: "Gemini");
            return new AiAnalysisOutcome(dto, model, LatencyMs(stopwatch), parsed.TotalTokenCount, parsed.FinishReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Any Gemini-path failure counts (D-025); caller cancellation propagates instead.
            _breaker.RecordFailure();
            _logger.LogWarning(
                "Gemini path failed for incident {IncidentId} ({ExceptionType}) after {LatencyMs} ms on model {Model} — falling back to rule-based: {Reason}",
                request.IncidentId, ex.GetType().Name, stopwatch.ElapsedMilliseconds, model, ex.Message);
            return await FallbackAsync(request, stopwatch, ct);
        }
    }

    private async Task<AiAnalysisOutcome> FallbackAsync(
        AiAnalysisRequest request, Stopwatch stopwatch, CancellationToken ct)
    {
        var dto = await _fallback.AnalyzeIncidentAsync(request, ct);
        stopwatch.Stop();
        return new AiAnalysisOutcome(dto, ModelName: null, LatencyMs(stopwatch), TokensUsed: null, FinishReason: null);
    }

    private static int LatencyMs(Stopwatch stopwatch)
        => (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);
}
