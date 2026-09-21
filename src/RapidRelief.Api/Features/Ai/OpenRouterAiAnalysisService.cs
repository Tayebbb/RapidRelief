using System.Diagnostics;
using RapidRelief.Api.Features.Ai.OpenRouter;
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
    string? FinishReason,
    AiFindings Findings,
    string? DegradedReason);

/// <summary>
/// D-028 composite provider chain: empty Ai:OpenRouter:ApiKey → straight to rule-based;
/// breaker open → rule-based; otherwise try OpenRouter and fall back on ANY failure while
/// counting it against the breaker — except a D-064 block (HTTP 403 or finish_reason
/// content_filter), which falls back WITHOUT counting and releases the half-open probe.
/// Never throws for analysis failures; logs metadata only (exception type, latency, model —
/// never description/photo/response text).
/// </summary>
internal sealed class OpenRouterAiAnalysisService : IAiAnalysisService
{
    private readonly RuleBasedAiAnalysisService _fallback;
    private readonly IOpenRouterClient _client;
    private readonly IFileStorage _fileStorage;
    private readonly AiCircuitBreaker _breaker;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenRouterAiAnalysisService> _logger;

    public OpenRouterAiAnalysisService(
        RuleBasedAiAnalysisService fallback,
        IOpenRouterClient client,
        IFileStorage fileStorage,
        AiCircuitBreaker breaker,
        TimeProvider timeProvider,
        IConfiguration config,
        ILogger<OpenRouterAiAnalysisService> logger)
    {
        _fallback = fallback;
        _client = client;
        _fileStorage = fileStorage;
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

        var apiKey = _config["Ai:OpenRouter:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // D-028: missing key never crashes and never counts against the breaker.
            return await FallbackAsync(request, stopwatch, ct, "No model provider is configured");
        }

        if (!_breaker.TryEnter())
        {
            _logger.LogInformation("OpenRouter breaker open — rule-based fallback for incident {IncidentId}",
                request.IncidentId);
            return await FallbackAsync(request, stopwatch, ct, "Model provider is temporarily circuit-broken");
        }

        IReadOnlyList<string> models = ModelsFor(isVision: false);
        try
        {
            // Blueprint chain: load first photo (D-024) → pick the D-061/D-062 model pair →
            // build request → client (D-026 timeout).
            var photo = await LoadFirstPhotoAsync(request, ct);
            models = ModelsFor(isVision: photo is not null);
            var requestBody = OpenRouterPromptBuilder.Build(request, photo, models);
            var responseBody = await _client.SendAsync(requestBody, photo is not null, ct);

            var result = OpenRouterResponseParser.Parse(responseBody);
            if (result.Status == AiParseStatus.Invalid)
            {
                throw new AiProviderUnavailableException($"Response rejected: {result.RejectReason}");
            }

            if (result.Status == AiParseStatus.Blocked)
            {
                // D-064: a content_filter finish is a normal outcome, not an availability failure.
                _breaker.AbandonProbe();
                _logger.LogInformation(
                    "OpenRouter blocked the request for incident {IncidentId} ({Reason}) — rule-based fallback",
                    request.IncidentId, result.RejectReason);
                return await FallbackAsync(request, stopwatch, ct, "Model provider declined to assess this report");
            }

            var parsed = result.Parsed!;
            stopwatch.Stop();
            _breaker.RecordSuccess();

            var severity = (Severity)parsed.Severity;
            var priority = PriorityFormula.Compute(severity, request.IsSos, request.ReportedAtUtc,
                _timeProvider.GetUtcNow());
            // Metadata only — never description/photo/response text (blueprint PII rule).
            // ModelName = response.model, the actually routed model (D-061).
            _logger.LogInformation(
                "OpenRouter assessed incident {IncidentId}: model {Model}, {LatencyMs} ms, {Tokens} tokens, confidence {Confidence:F2}",
                request.IncidentId, parsed.ModelName, stopwatch.ElapsedMilliseconds, parsed.TotalTokenCount, parsed.Confidence);

            var dto = new AiAssessmentDto(request.IncidentId, parsed.PredictedType, severity,
                priority, parsed.Summary, PossibleDuplicateOfId: null, Provider: "OpenRouter");
            return new AiAnalysisOutcome(dto, parsed.ModelName, LatencyMs(stopwatch), parsed.TotalTokenCount,
                parsed.FinishReason, Merge(request, parsed, photo is not null), DegradedReason: null);
        }
        catch (AiProviderBlockedException ex)
        {
            // D-064: HTTP 403 = input moderation — canned outcome, no breaker count, probe freed.
            _breaker.AbandonProbe();
            _logger.LogInformation(
                "OpenRouter flagged the input for incident {IncidentId} ({Reason}) — rule-based fallback",
                request.IncidentId, ex.Message);
            return await FallbackAsync(request, stopwatch, ct, "Model provider flagged the report text");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancellation escaping between TryEnter and Record* (photo load or client
            // call) would otherwise strand a half-open probe forever — release it, then rethrow.
            _breaker.AbandonProbe();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Any provider-path failure counts (D-025); caller cancellation propagates instead.
            _breaker.RecordFailure();
            _logger.LogWarning(
                "OpenRouter path failed for incident {IncidentId} ({ExceptionType}) after {LatencyMs} ms on model {Model} — falling back to rule-based: {Reason}",
                request.IncidentId, ex.GetType().Name, stopwatch.ElapsedMilliseconds, models[0], ex.Message);
            return await FallbackAsync(request, stopwatch, ct, $"Model provider unavailable ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// The model supplies judgement; the deterministic reader supplies evidence. Union the two so
    /// a terse model answer still carries the indicators the text plainly contains, and a reported
    /// head count is never overwritten by a lower guess.
    /// </summary>
    private AiFindings Merge(AiAnalysisRequest request, ParsedAssessment parsed, bool sawPhoto)
    {
        var signals = IncidentSignalReader.Read(request.Description);

        var indicators = parsed.DamageIndicators
            .Concat(signals.DamageIndicators)
            .Select(i => i.Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var people = request.AffectedPeopleCount > 0
            ? Math.Max(request.AffectedPeopleCount, parsed.EstimatedPeopleAffected ?? 0)
            : parsed.EstimatedPeopleAffected ?? signals.PeopleMentioned;

        var reasoning = string.IsNullOrWhiteSpace(parsed.Reasoning)
            ? $"Model {parsed.ModelName ?? "response"} returned {parsed.PredictedType} at severity {parsed.Severity}/5 without stating its evidence."
            : parsed.Reasoning;
        reasoning += sawPhoto
            ? " A photo from the report was analysed."
            : " No photo was available, so this is a text-only assessment.";

        return new AiFindings(parsed.PredictedType, (Severity)parsed.Severity, parsed.Confidence,
            indicators, people, parsed.MedicalUrgency || signals.MedicalUrgency,
            parsed.Summary, reasoning);
    }

    /// <summary>D-061/D-062 model pairs from config; empty fallback ⇒ single-element array.</summary>
    private IReadOnlyList<string> ModelsFor(bool isVision)
    {
        var primary = isVision
            ? _config["Ai:OpenRouter:VisionModel"] ?? "google/gemma-4-31b-it:free"
            : _config["Ai:OpenRouter:TextModel"] ?? "z-ai/glm-5.2:free";
        var fallback = isVision
            ? _config["Ai:OpenRouter:VisionFallbackModel"]
            : _config["Ai:OpenRouter:TextFallbackModel"];
        return string.IsNullOrWhiteSpace(fallback) ? [primary] : [primary, fallback];
    }

    /// <summary>D-024: any photo problem degrades to text-only — never fails the pipeline.</summary>
    private async Task<AiPhoto?> LoadFirstPhotoAsync(AiAnalysisRequest request, CancellationToken ct)
    {
        if (request.PhotoPaths is not { Count: > 0 } paths)
        {
            return null;
        }

        if (paths.Count > 1)
        {
            _logger.LogInformation(
                "Dropping {DroppedCount} extra photo(s) for incident {IncidentId} — first photo only (D-024)",
                paths.Count - 1, request.IncidentId);
        }

        var path = paths[0];
        var mimeType = MimeFromExtension(path);
        if (mimeType is null)
        {
            _logger.LogWarning(
                "Photo for incident {IncidentId} has unsupported extension {Extension} — proceeding text-only",
                request.IncidentId, Path.GetExtension(path));
            return null;
        }

        try
        {
            await using var stream = await _fileStorage.OpenReadAsync(path, ct);
            if (stream is null)
            {
                _logger.LogWarning(
                    "Photo for incident {IncidentId} not found in storage — proceeding text-only",
                    request.IncidentId);
                return null;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return new AiPhoto(mimeType, Convert.ToBase64String(buffer.ToArray()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Metadata only — never the photo bytes; a broken photo must not become a breaker failure.
            _logger.LogWarning(
                "Photo read failed for incident {IncidentId} ({ExceptionType}) — proceeding text-only",
                request.IncidentId, ex.GetType().Name);
            return null;
        }
    }

    // D-024 whitelist: only extensions the upload path can produce for images.
    private static string? MimeFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null,
    };

    private async Task<AiAnalysisOutcome> FallbackAsync(
        AiAnalysisRequest request, Stopwatch stopwatch, CancellationToken ct, string degradedReason)
    {
        var findings = _fallback.Analyze(request);
        var dto = await _fallback.AnalyzeIncidentAsync(request, ct);
        stopwatch.Stop();
        return new AiAnalysisOutcome(dto, ModelName: null, LatencyMs(stopwatch), TokensUsed: null,
            FinishReason: null, findings, degradedReason);
    }

    private static int LatencyMs(Stopwatch stopwatch)
        => (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);
}
