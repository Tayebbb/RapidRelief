using RapidRelief.Api.Features.Stubs;
using RapidRelief.Api.Features.Stubs.SeedData;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Stubs;

public sealed class FakeIncidentReadServiceTests
{
    private readonly FakeIncidentReadService _service = new();

    [Fact]
    public async Task Unfiltered_query_returns_every_seed_incident_in_total_count()
    {
        var result = await _service.GetIncidentsAsync(new IncidentQuery(PageSize: 100));

        Assert.Equal(DhakaSeedData.Incidents.Count, result.TotalCount);
        Assert.Equal(DhakaSeedData.Incidents.Count, result.Items.Count);
    }

    [Fact]
    public async Task Status_filter_returns_only_matching_incidents()
    {
        var result = await _service.GetIncidentsAsync(new IncidentQuery(Status: IncidentStatus.Reported, PageSize: 100));

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.Equal(IncidentStatus.Reported, i.Status));
        Assert.Equal(DhakaSeedData.Incidents.Count(i => i.Status == IncidentStatus.Reported), result.TotalCount);
    }

    [Fact]
    public async Task Type_filter_returns_only_matching_incidents()
    {
        var result = await _service.GetIncidentsAsync(new IncidentQuery(Type: DisasterType.Flood, PageSize: 100));

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.Equal(DisasterType.Flood, i.Type));
    }

    [Fact]
    public async Task MinSeverity_filter_is_inclusive_lower_bound()
    {
        var result = await _service.GetIncidentsAsync(new IncidentQuery(MinSeverity: Severity.Severe, PageSize: 100));

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.Severity >= Severity.Severe));
        Assert.Equal(DhakaSeedData.Incidents.Count(i => i.Severity >= Severity.Severe), result.TotalCount);
    }

    [Fact]
    public async Task Paging_slices_items_but_total_count_stays_the_full_filtered_count()
    {
        var pageOne = await _service.GetIncidentsAsync(new IncidentQuery(Page: 1, PageSize: 10));
        var pageTwo = await _service.GetIncidentsAsync(new IncidentQuery(Page: 2, PageSize: 10));
        var pageNine = await _service.GetIncidentsAsync(new IncidentQuery(Page: 9, PageSize: 10));

        Assert.Equal(10, pageOne.Items.Count);
        Assert.Equal(10, pageTwo.Items.Count);
        Assert.Empty(pageNine.Items);
        Assert.Equal(DhakaSeedData.Incidents.Count, pageOne.TotalCount);
        Assert.Equal(DhakaSeedData.Incidents.Count, pageTwo.TotalCount);
        Assert.Equal(DhakaSeedData.Incidents.Count, pageNine.TotalCount);
        Assert.Empty(pageOne.Items.Select(i => i.Id).Intersect(pageTwo.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_incident_for_a_known_id_and_null_for_unknown()
    {
        var known = DhakaSeedData.NearDuplicateIncidentIdA;

        var found = await _service.GetByIdAsync(known);
        var missing = await _service.GetByIdAsync(Guid.Parse("99999999-9999-9999-9999-999999999999"));

        Assert.NotNull(found);
        Assert.Equal(known, found!.Id);
        Assert.Null(missing);
    }
}
