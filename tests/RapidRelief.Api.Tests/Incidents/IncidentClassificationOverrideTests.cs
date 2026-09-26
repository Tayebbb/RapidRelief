using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Audit.Data;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Incidents.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Incidents;

public sealed class IncidentClassificationOverrideTests : IClassFixture<TestingWebAppFactory>
{
    private const string BasePath = "/api/incidents";
    private readonly TestingWebAppFactory _factory;

    public IncidentClassificationOverrideTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        await incidents.StatusHistory.ExecuteDeleteAsync();
        await incidents.Media.ExecuteDeleteAsync();
        await incidents.Reports.ExecuteDeleteAsync();
        await audit.Entries.ExecuteDeleteAsync();
    }

    private async Task<Guid> CreateIncidentAsync(DisasterType type = DisasterType.Flood, Severity severity = Severity.Minor)
    {
        var citizen = Client(Roles.Citizen);
        var response = await citizen.PostAsJsonAsync(BasePath, new CreateIncidentRequest(
            Title: "Water issue reported",
            Description: "Ground floor water leak in basement area.",
            DisasterType: type,
            Severity: severity,
            Latitude: 23.8103,
            Longitude: 90.4125,
            AddressOrArea: "Mirpur 10",
            AffectedPeopleCount: 2,
            IsSos: false,
            ContactPhone: "+8801700000000",
            PhotoPaths: null,
            IdempotencyKey: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();
        Assert.NotNull(envelope?.Data);
        return envelope!.Data!.Id;
    }

    [Fact]
    public async Task Government_officer_can_override_disaster_type_and_severity()
    {
        await ResetAsync();
        var incidentId = await CreateIncidentAsync(DisasterType.Flood, Severity.Minor);
        var gov = Client(Roles.Government);

        var overrideReq = new OverrideClassificationRequest(
            DisasterType: DisasterType.BuildingCollapse,
            Severity: Severity.Catastrophic,
            AdjustedPriorityScore: 95.0,
            Reason: "Ground inspection confirmed structural collapse, not minor flooding."
        );

        var response = await gov.PostAsJsonAsync($"{BasePath}/{incidentId}/override", overrideReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();
        Assert.NotNull(envelope?.Data);
        var dto = envelope!.Data!;

        Assert.Equal(DisasterType.BuildingCollapse, dto.DisasterType);
        Assert.Equal(Severity.Catastrophic, dto.Severity);
        Assert.Equal(95.0, dto.PriorityScore);
        Assert.True(dto.IsClassificationOverridden);
        Assert.Equal("Ground inspection confirmed structural collapse, not minor flooding.", dto.OverrideReason);
        Assert.NotNull(dto.OverriddenAtUtc);
        Assert.Equal(FakeAuthHandler.SeedUserIds[Roles.Government], dto.OverriddenByGovernmentId);

        // Verify in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
        var incidentInDb = await db.Reports.Include(r => r.StatusHistory).FirstAsync(r => r.Id == incidentId);

        Assert.Equal(DisasterType.BuildingCollapse, incidentInDb.DisasterType);
        Assert.Equal(Severity.Catastrophic, incidentInDb.Severity);
        Assert.Equal(95.0, incidentInDb.PriorityScore);
        Assert.True(incidentInDb.IsClassificationOverridden);
        Assert.Equal("Ground inspection confirmed structural collapse, not minor flooding.", incidentInDb.OverrideReason);
        Assert.Equal(FakeAuthHandler.SeedUserIds[Roles.Government], incidentInDb.OverriddenByGovernmentId);

        // Verify StatusHistory entry
        Assert.Contains(incidentInDb.StatusHistory, h =>
            h.Notes.Contains("Classification manually overridden by command") &&
            h.Notes.Contains("Flood/Minor -> BuildingCollapse/Catastrophic"));

        // Verify Audit Trail entry
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditEntry = await auditDb.Entries
            .Where(e => e.EntityType == "Incident" && e.EntityId == incidentId.ToString())
            .FirstOrDefaultAsync(e => e.Action == "Incident.ClassificationOverride");

        Assert.NotNull(auditEntry);
        Assert.Equal(Roles.Government, auditEntry.ActorRole);
        Assert.Contains("Overrode Flood to BuildingCollapse", auditEntry.Summary);
    }

    [Theory]
    [InlineData(Roles.Citizen)]
    [InlineData(Roles.Rescuer)]
    public async Task Rescuer_and_Citizen_cannot_override_classification(string role)
    {
        await ResetAsync();
        var incidentId = await CreateIncidentAsync();
        var client = Client(role);

        var overrideReq = new OverrideClassificationRequest(
            DisasterType: DisasterType.Fire,
            Severity: Severity.Severe,
            AdjustedPriorityScore: 80.0,
            Reason: "Attempted unauthorized override."
        );

        var response = await client.PostAsJsonAsync($"{BasePath}/{incidentId}/override", overrideReq);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Overriding_resolved_incident_returns_409_conflict()
    {
        await ResetAsync();
        var incidentId = await CreateIncidentAsync();
        var gov = Client(Roles.Government);

        // Resolve incident
        var resolveResp = await gov.PostAsJsonAsync($"{BasePath}/{incidentId}/resolve", new ResolveIncidentRequest("Handled and closed."));
        Assert.Equal(HttpStatusCode.OK, resolveResp.StatusCode);

        // Attempt override
        var overrideReq = new OverrideClassificationRequest(
            DisasterType: DisasterType.Cyclone,
            Severity: Severity.Severe,
            AdjustedPriorityScore: null,
            Reason: "Trying to override closed incident."
        );

        var response = await gov.PostAsJsonAsync($"{BasePath}/{incidentId}/override", overrideReq);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Validation_fails_on_empty_reason()
    {
        await ResetAsync();
        var incidentId = await CreateIncidentAsync();
        var gov = Client(Roles.Government);

        var overrideReq = new OverrideClassificationRequest(
            DisasterType: DisasterType.Earthquake,
            Severity: Severity.Severe,
            AdjustedPriorityScore: 50.0,
            Reason: "" // Empty reason is invalid
        );

        var response = await gov.PostAsJsonAsync($"{BasePath}/{incidentId}/override", overrideReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Validation_fails_on_reason_exceeding_500_chars()
    {
        await ResetAsync();
        var incidentId = await CreateIncidentAsync();
        var gov = Client(Roles.Government);

        var longReason = new string('x', 501);
        var overrideReq = new OverrideClassificationRequest(
            DisasterType: DisasterType.Earthquake,
            Severity: Severity.Severe,
            AdjustedPriorityScore: 50.0,
            Reason: longReason
        );

        var response = await gov.PostAsJsonAsync($"{BasePath}/{incidentId}/override", overrideReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
