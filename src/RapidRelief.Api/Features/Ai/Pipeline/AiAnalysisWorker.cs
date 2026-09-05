using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai.Pipeline;

/// <summary>
/// D-021 consumer: one scope per item; analyze → duplicate-check → persist (idempotent on the
/// unique IncidentId index) → publish IncidentAssessed. Degraded DB (D-028): still analyzes
/// and publishes, skips persist, logs. A per-item failure never kills the worker.
/// </summary>
public sealed class AiAnalysisWorker : BackgroundService
{
    private readonly Channel<AiWorkItem> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AiAnalysisWorker> _logger;

    public AiAnalysisWorker(
        Channel<AiWorkItem> channel,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AiAnalysisWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Honor the stopping token in ReadAllAsync (blueprint risk 10) — cancellation exits
        // the loop; anything else is a per-item failure that must not kill the worker.
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI analysis pipeline failed for incident {IncidentId}",
                    item.Request.IncidentId);
            }
        }
    }

    private async Task ProcessAsync(AiWorkItem item, CancellationToken ct)
    {
        var request = item.Request;
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<Data.AiDbContext>();
        var health = services.GetRequiredService<DatabaseHealth>();
        var analysis = services.GetRequiredService<IAiAnalysisService>();
        var bus = services.GetRequiredService<IEventBus>();

        var degraded = health.PostgresAvailable != true;

        // Redelivery = silent skip, no publish (the first delivery already published).
        if (!degraded && await db.Assessments.AnyAsync(a => a.IncidentId == request.IncidentId, ct))
        {
            _logger.LogInformation("Incident {IncidentId} already assessed — skipping redelivery",
                request.IncidentId);
            return;
        }

        var outcome = await AnalyzeAsync(analysis, request, ct);
        var assessment = outcome.Assessment;
        var findings = outcome.Findings;

        DuplicateMatch? duplicate = null;
        if (!degraded)
        {
            duplicate = await services.GetRequiredService<DuplicateDetector>().FindMatchAsync(
                request.IncidentId, request.Location, request.ReportedType, request.ReportedAtUtc,
                request.Description, ct);
        }

        var duplicateOf = duplicate?.IncidentId;

        // Context the analysers cannot see: how crowded this area already is and whether anyone
        // is actually free to go. Both fail soft — a priority score must always be computable.
        var nearbyOpen = degraded ? 0 : await NearbyOpenIncidentsAsync(services, request, ct);
        var responders = await ResponderAvailabilityAsync(services, request, ct);

        var priority = IncidentPriorityEngine.Compute(new PriorityInputs(
            findings.EstimatedSeverity,
            request.IsSos,
            findings.EstimatedPeopleAffected ?? request.AffectedPeopleCount,
            findings.MedicalUrgency,
            request.ReportedAtUtc,
            _timeProvider.GetUtcNow(),
            findings.Confidence,
            nearbyOpen,
            responders));

        var summary = assessment.Summary.Length <= 200 ? assessment.Summary : assessment.Summary[..200];

        if (degraded)
        {
            // D-028: analysis + event survive the outage; only persistence is skipped.
            _logger.LogWarning(
                "Database degraded — assessment for incident {IncidentId} published but not persisted",
                request.IncidentId);
        }
        else
        {
            db.Assessments.Add(new Domain.AiAssessment
            {
                Id = Guid.NewGuid(),
                IncidentId = request.IncidentId,
                PredictedType = assessment.PredictedType,
                EstimatedSeverity = assessment.EstimatedSeverity,
                PriorityScore = priority.Score,
                Summary = summary,
                PossibleDuplicateOfId = duplicateOf,
                Provider = assessment.Provider,
                ModelName = outcome.ModelName,
                LatencyMs = outcome.LatencyMs,
                TokensUsed = outcome.TokensUsed,
                FinishReason = outcome.FinishReason,
                Confidence = findings.Confidence,
                Urgency = priority.Urgency,
                PriorityBand = priority.Band,
                EstimatedPeopleAffected = findings.EstimatedPeopleAffected,
                MedicalUrgency = findings.MedicalUrgency,
                DamageIndicatorsJson = JsonSerializer.Serialize(findings.DamageIndicators),
                Reasoning = Clamp($"{findings.Reasoning} {priority.Explanation}", 600),
                PriorityFactorsJson = JsonSerializer.Serialize(priority.Factors),
                DegradedReason = outcome.DegradedReason,
                DuplicateConfidence = duplicate?.Confidence,
                DuplicateReason = duplicate?.Reason,
                SnapshotLatitude = request.Location.Latitude,
                SnapshotLongitude = request.Location.Longitude,
                SnapshotType = request.ReportedType,
                SnapshotReportedAtUtc = request.ReportedAtUtc,
                SnapshotIsSos = request.IsSos,
                SnapshotDescriptionKey = Clamp(IncidentSignalReader.Normalise(request.Description), 600),
                CreatedAtUtc = _timeProvider.GetUtcNow(),
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Unique IncidentId index lost a race — already assessed elsewhere; no publish.
                _logger.LogInformation(ex,
                    "Assessment for incident {IncidentId} already persisted (unique-index race) — skipping publish",
                    request.IncidentId);
                return;
            }
        }

        // CancellationToken.None on purpose: after a successful SaveChangesAsync the row
        // exists — a shutdown cancellation in this gap would persist the assessment but lose
        // IncidentAssessed forever (no redelivery once the row blocks reprocessing).
        await bus.PublishAsync(new IncidentAssessed(request.IncidentId, assessment.EstimatedSeverity,
            priority.Score, summary, duplicateOf), CancellationToken.None);
        _logger.LogInformation(
            "Incident {IncidentId} assessed by {Provider}: severity {Severity}, priority {Priority:F0} ({Band}), duplicateOf {DuplicateOf}",
            request.IncidentId, assessment.Provider, (int)assessment.EstimatedSeverity,
            priority.Score, priority.Band, duplicateOf);
    }

    /// <summary>Other open incidents within 2 km — a cluster means the area is deteriorating.</summary>
    private async Task<int> NearbyOpenIncidentsAsync(
        IServiceProvider services, AiAnalysisRequest request, CancellationToken ct)
    {
        try
        {
            var incidents = services.GetRequiredService<IIncidentReadService>();
            var nearby = await incidents.GetIncidentsAsync(
                new IncidentQuery(Status: null, Near: request.Location, RadiusKm: 2, PageSize: 25), ct);
            return nearby.Items.Count(x => x.Id != request.IncidentId
                && x.Status is not (IncidentStatus.Resolved or IncidentStatus.Rejected));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Nearby-incident lookup failed for {IncidentId} — scoring without it",
                request.IncidentId);
            return 0;
        }
    }

    private async Task<ResponderAvailabilityDto> ResponderAvailabilityAsync(
        IServiceProvider services, AiAnalysisRequest request, CancellationToken ct)
    {
        try
        {
            return await services.GetRequiredService<IResponderAvailabilityService>()
                .GetAvailabilityAsync(request.Location, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Responder availability lookup failed for {IncidentId} — scoring without it",
                request.IncidentId);
            return ResponderAvailabilityDto.Unknown;
        }
    }

    private static string Clamp(string value, int max)
        => value.Length <= max ? value : value[..max];

    /// <summary>Prefers the composite's telemetry-rich path; any substituted service still works.</summary>
    private static async Task<AiAnalysisOutcome> AnalyzeAsync(
        IAiAnalysisService analysis, AiAnalysisRequest request, CancellationToken ct)
    {
        if (analysis is OpenRouterAiAnalysisService composite)
        {
            return await composite.AnalyzeWithMetadataAsync(request, ct);
        }

        var stopwatch = Stopwatch.StartNew();
        var dto = await analysis.AnalyzeIncidentAsync(request, ct);
        stopwatch.Stop();

        // A substituted analyser only fills the frozen DTO, so the structured view is rebuilt
        // from what it returned plus the deterministic reader — never left empty.
        var signals = IncidentSignalReader.Read(request.Description);
        var findings = new AiFindings(dto.PredictedType, dto.EstimatedSeverity, 0.5,
            signals.DamageIndicators,
            request.AffectedPeopleCount > 0 ? request.AffectedPeopleCount : signals.PeopleMentioned,
            signals.MedicalUrgency, dto.Summary,
            $"Assessed by {dto.Provider} without structured reasoning; evidence taken from the report text.");

        return new AiAnalysisOutcome(dto, ModelName: null,
            (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue), TokensUsed: null,
            FinishReason: null, findings, DegradedReason: null);
    }
}
