using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Features.Ai.Pipeline;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN item 5 / D-022: Haversine ≤ 300 m ∧ same declared type ∧
/// |Δt| ≤ 30 min ∧ not self; nearest wins; Resolved/Rejected candidates disqualified via
/// the read service; unknown (null) candidates stay.
/// </summary>
public sealed class DuplicateDetectorTests : IDisposable
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // Seed-data analogs: incidents 5 and 6 (~130 m apart), incident 1 (~215 m from 5, Δt 60 min).
    private static readonly GeoPoint PairA = new(23.8225, 90.3652);
    private static readonly GeoPoint PairB = new(23.8235, 90.3660);

    private readonly SqliteConnection _connection;
    private readonly AiDbContext _db;

    private sealed class StubIncidentReadService : IIncidentReadService
    {
        public Dictionary<Guid, IncidentSummaryDto> Incidents { get; } = [];

        public Task<PagedResult<IncidentSummaryDto>> GetIncidentsAsync(IncidentQuery query, CancellationToken ct = default)
            => throw new NotSupportedException("Detector must only use GetByIdAsync.");

        public Task<IncidentSummaryDto?> GetByIdAsync(Guid incidentId, CancellationToken ct = default)
            => Task.FromResult(Incidents.TryGetValue(incidentId, out var incident) ? incident : null);
    }

    private readonly StubIncidentReadService _incidents = new();

    public DuplicateDetectorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AiDbContext>().UseSqlite(_connection).Options;
        _db = new AiDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private DuplicateDetector CreateDetector() => new(_db, _incidents);

    private Guid AddAssessment(GeoPoint location, DisasterType type, DateTimeOffset reportedAtUtc, Guid? incidentId = null)
    {
        var id = incidentId ?? Guid.NewGuid();
        _db.Assessments.Add(new AiAssessment
        {
            Id = Guid.NewGuid(),
            IncidentId = id,
            PredictedType = type,
            EstimatedSeverity = Severity.Moderate,
            PriorityScore = 50,
            Summary = "seeded candidate",
            Provider = "RuleBased",
            LatencyMs = 1,
            SnapshotLatitude = location.Latitude,
            SnapshotLongitude = location.Longitude,
            SnapshotType = type,
            SnapshotReportedAtUtc = reportedAtUtc,
            SnapshotIsSos = false,
            CreatedAtUtc = reportedAtUtc,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return id;
    }

    private static IncidentSummaryDto Incident(Guid id, IncidentStatus status)
        => new(id, DisasterType.Flood, Severity.Moderate, status, PairA, "status probe",
            Anchor.AddHours(-1), false, null);

    [Fact]
    public async Task Seeded_pair_analog_links_to_the_130m_20min_neighbour()
    {
        // Existing assessment = incident 5's snapshot; query = incident 6's data.
        var candidateId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0));

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-4.0 / 3));

        Assert.Equal(candidateId, result);
    }

    [Fact]
    public async Task Sixty_minute_gap_is_not_a_duplicate_even_at_215m()
    {
        // Incident 1 vs incident 5 analog: same type, ~215 m, Δt 60 min → outside the window.
        AddAssessment(new GeoPoint(23.8210, 90.3665), DisasterType.Flood, Anchor.AddHours(-2.0));

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairA, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Null(result);
    }

    [Fact]
    public async Task Beyond_300_meters_is_not_a_duplicate()
    {
        // ~555 m north — same type, same minute.
        AddAssessment(new GeoPoint(23.8275, 90.3652), DisasterType.Flood, Anchor.AddHours(-1.0));

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairA, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Null(result);
    }

    [Fact]
    public async Task Different_declared_type_is_not_a_duplicate()
    {
        AddAssessment(PairA, DisasterType.Fire, Anchor.AddHours(-1.0));

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Null(result);
    }

    [Fact]
    public async Task Own_assessment_row_is_excluded()
    {
        var selfId = Guid.NewGuid();
        AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0), incidentId: selfId);

        var result = await CreateDetector().FindDuplicateAsync(
            selfId, PairA, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Null(result);
    }

    [Fact]
    public async Task Nearest_candidate_wins_when_several_qualify()
    {
        var farId = AddAssessment(new GeoPoint(23.8250, 90.3652), DisasterType.Flood, Anchor.AddHours(-1.0)); // ~185 m
        var nearId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0)); // ~137 m

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Equal(nearId, result);
        Assert.NotEqual(farId, result);
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Rejected)]
    public async Task Closed_out_candidates_are_disqualified_by_the_status_recheck(IncidentStatus status)
    {
        var candidateId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0));
        _incidents.Incidents[candidateId] = Incident(candidateId, status);

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Null(result);
    }

    [Fact]
    public async Task Disqualified_nearest_falls_through_to_the_next_candidate()
    {
        var nearId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0)); // ~137 m
        var farId = AddAssessment(new GeoPoint(23.8250, 90.3652), DisasterType.Flood, Anchor.AddHours(-1.0)); // ~185 m
        _incidents.Incidents[nearId] = Incident(nearId, IncidentStatus.Resolved);

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Equal(farId, result);
    }

    [Theory]
    [InlineData(IncidentStatus.Reported)]
    [InlineData(IncidentStatus.Verified)]
    [InlineData(IncidentStatus.InProgress)]
    public async Task Open_statuses_keep_the_candidate(IncidentStatus status)
    {
        var candidateId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0));
        _incidents.Incidents[candidateId] = Incident(candidateId, status);

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Equal(candidateId, result);
    }

    [Fact]
    public async Task Unknown_incident_status_keeps_the_candidate()
    {
        // Read service knows nothing about pipeline-created incidents → null must NOT disqualify.
        var candidateId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0));

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Equal(candidateId, result);
    }

    [Fact]
    public async Task Empty_table_returns_null()
    {
        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Null(result);
    }

    [Fact]
    public async Task Exactly_thirty_minutes_apart_is_still_a_duplicate()
    {
        // D-022 pins |Δt| ≤ 30 min as INCLUSIVE — the boundary itself links.
        var candidateId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0));

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), PairB, DisasterType.Flood,
            Anchor.AddHours(-1.0) + TimeSpan.FromMinutes(30));

        Assert.Equal(candidateId, result);
    }

    [Fact]
    public async Task At_the_300_meter_boundary_is_still_a_duplicate()
    {
        // D-022 pins Haversine ≤ 300 m as INCLUSIVE. A pure-north offset targeting
        // 299.9995 m lands within fp noise of the boundary from below; the premise
        // assert keeps the test honest if GeoMath or the constant ever drifts.
        const double latDeltaDegrees = 299.9995 / 6_371_000 * 180.0 / Math.PI;
        var origin = new GeoPoint(PairA.Latitude + latDeltaDegrees, PairA.Longitude);
        var distance = GeoMath.HaversineMeters(origin, PairA);
        Assert.InRange(distance, 299.999, 300.0);

        var candidateId = AddAssessment(PairA, DisasterType.Flood, Anchor.AddHours(-1.0));

        var result = await CreateDetector().FindDuplicateAsync(
            Guid.NewGuid(), origin, DisasterType.Flood, Anchor.AddHours(-1.0));

        Assert.Equal(candidateId, result);
    }
}
