using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Audit.Data;
using RapidRelief.Api.Features.Registry.Data;
using RapidRelief.Api.Features.Registry.Endpoints;
using RapidRelief.Api.Features.Registry.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;
using Xunit;

namespace RapidRelief.Api.Tests.Registry;

public sealed class RegistryTests : IClassFixture<TestingWebAppFactory>
{
    private const string BasePath = "/api/registry";

    private readonly TestingWebAppFactory _factory;

    public RegistryTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient Client(string? role = null)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        }
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        await db.Hospitals.ExecuteDeleteAsync();
        await db.Volunteers.ExecuteDeleteAsync();
        await db.Ngos.ExecuteDeleteAsync();
        await audit.Entries.ExecuteDeleteAsync();

        await RegistrySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
    }

    [Fact]
    public async Task Seeder_populates_initial_dhaka_data_and_is_idempotent()
    {
        await ResetAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            Assert.Equal(6, await db.Hospitals.CountAsync());
            Assert.Equal(10, await db.Volunteers.CountAsync());
            Assert.Equal(5, await db.Ngos.CountAsync());

            // Run seeder again — counts must remain unchanged
            await RegistrySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
            Assert.Equal(6, await db.Hospitals.CountAsync());
            Assert.Equal(10, await db.Volunteers.CountAsync());
            Assert.Equal(5, await db.Ngos.CountAsync());
        }
    }

    [Fact]
    public async Task RegistryReadService_reads_from_database_and_displaces_fake()
    {
        await ResetAsync();

        using var scope = _factory.Services.CreateScope();
        var readService = scope.ServiceProvider.GetRequiredService<IRegistryReadService>();

        // Real RegistryReadService must displace FakeRegistryReadService under stub-yield rule
        Assert.IsType<RegistryReadService>(readService);

        var hospitals = await readService.GetHospitalsAsync();
        Assert.Equal(6, hospitals.Count);
        Assert.Contains(hospitals, h => h.Name.Contains("Dhaka Medical College"));

        var volunteers = await readService.GetVolunteersAsync();
        Assert.Equal(10, volunteers.Count);
        Assert.Contains(volunteers, v => v.Name == "Arif Hossain");

        var ngos = await readService.GetNgosAsync();
        Assert.Equal(5, ngos.Count);
        Assert.Contains(ngos, n => n.Name.Contains("BRAC"));
    }

    [Fact]
    public async Task Hospital_crud_lifecycle_and_validation()
    {
        await ResetAsync();
        var gov = Client(Roles.Government);

        // Validation failure: available beds > total beds
        var invalidResp = await gov.PostAsJsonAsync($"{BasePath}/hospitals", new CreateHospitalRequest(
            Name: "Invalid Hospital",
            Latitude: 23.81,
            Longitude: 90.41,
            TotalBeds: 50,
            AvailableBeds: 100, // Invalid
            IcuBeds: 10,
            AvailableIcuBeds: 2,
            HasEmergency: true,
            Specialties: ["Trauma"],
            ContactNumber: "+880 2 123456",
            Address: "Sector 5"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResp.StatusCode);

        // Create hospital
        var createResp = await gov.PostAsJsonAsync($"{BasePath}/hospitals", new CreateHospitalRequest(
            Name: "Dhanmondi General Hospital",
            Latitude: 23.7465,
            Longitude: 90.3750,
            TotalBeds: 150,
            AvailableBeds: 45,
            IcuBeds: 15,
            AvailableIcuBeds: 3,
            HasEmergency: true,
            Specialties: ["Trauma", "Cardiology"],
            ContactNumber: "+880 2 987654",
            Address: "Road 7, Dhanmondi"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = (await createResp.Content.ReadFromJsonAsync<ApiEnvelope<HospitalDto>>())!.Data!;
        Assert.Equal("Dhanmondi General Hospital", created.Name);

        // Read by ID
        var getResp = await gov.GetAsync($"{BasePath}/hospitals/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var readDto = (await getResp.Content.ReadFromJsonAsync<ApiEnvelope<HospitalDto>>())!.Data!;
        Assert.Equal(45, readDto.AvailableBeds);

        // Update hospital
        var updateResp = await gov.PutAsJsonAsync($"{BasePath}/hospitals/{created.Id}", new UpdateHospitalRequest(
            Name: "Dhanmondi General Hospital (Upgraded)",
            Latitude: 23.7465,
            Longitude: 90.3750,
            TotalBeds: 200,
            AvailableBeds: 60,
            IcuBeds: 20,
            AvailableIcuBeds: 5,
            HasEmergency: true,
            Specialties: ["Trauma", "Cardiology", "Burn"],
            ContactNumber: "+880 2 987654",
            Address: "Road 7, Dhanmondi"));
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = (await updateResp.Content.ReadFromJsonAsync<ApiEnvelope<HospitalDto>>())!.Data!;
        Assert.Equal("Dhanmondi General Hospital (Upgraded)", updated.Name);
        Assert.Equal(60, updated.AvailableBeds);

        // Delete hospital
        var deleteResp = await gov.DeleteAsync($"{BasePath}/hospitals/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // Verify 404 after delete
        var getAfterDelete = await gov.GetAsync($"{BasePath}/hospitals/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);

        // Verify audit entries
        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var entries = await audit.Entries.Where(e => e.EntityType == "Hospital" && e.EntityId == created.Id.ToString()).ToListAsync();
        Assert.Contains(entries, e => e.Action == "Registry.HospitalCreate");
        Assert.Contains(entries, e => e.Action == "Registry.HospitalUpdate");
        Assert.Contains(entries, e => e.Action == "Registry.HospitalDelete");
    }

    [Fact]
    public async Task Volunteer_crud_lifecycle_and_validation()
    {
        await ResetAsync();
        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);

        // Validation failure: invalid latitude
        var invalidResp = await citizen.PostAsJsonAsync($"{BasePath}/volunteers", new CreateVolunteerRequest(
            FullName: "Test Volunteer",
            UserId: null,
            Latitude: 500, // Invalid
            Longitude: 90.4,
            Skills: ["FirstAid"],
            Status: "Available",
            ContactNumber: "+880 1234"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResp.StatusCode);

        // Create volunteer
        var createResp = await citizen.PostAsJsonAsync($"{BasePath}/volunteers", new CreateVolunteerRequest(
            FullName: "Kazi Nazrul",
            UserId: null,
            Latitude: 23.8150,
            Longitude: 90.4150,
            Skills: ["FirstAid", "SearchAndRescue"],
            Status: "Available",
            ContactNumber: "+880 1711 000111"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = (await createResp.Content.ReadFromJsonAsync<ApiEnvelope<VolunteerDto>>())!.Data!;
        Assert.Equal("Kazi Nazrul", created.FullName);

        // Read by ID
        var getResp = await citizen.GetAsync($"{BasePath}/volunteers/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        // Update status to Assigned
        var updateResp = await citizen.PutAsJsonAsync($"{BasePath}/volunteers/{created.Id}", new UpdateVolunteerRequest(
            FullName: "Kazi Nazrul",
            Latitude: 23.8150,
            Longitude: 90.4150,
            Skills: ["FirstAid", "SearchAndRescue", "Diving"],
            Status: "Assigned",
            ContactNumber: "+880 1711 000111"));
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = (await updateResp.Content.ReadFromJsonAsync<ApiEnvelope<VolunteerDto>>())!.Data!;
        Assert.Equal("Assigned", updated.Status);

        // Delete volunteer via Government
        var deleteResp = await gov.DeleteAsync($"{BasePath}/volunteers/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getAfterDelete = await citizen.GetAsync($"{BasePath}/volunteers/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task Ngo_crud_lifecycle_and_validation()
    {
        await ResetAsync();
        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);

        // Create NGO
        var createResp = await citizen.PostAsJsonAsync($"{BasePath}/ngos", new CreateNgoRequest(
            Name: "Dhaka Relief Trust",
            Latitude: 23.8000,
            Longitude: 90.4000,
            FocusAreas: ["Emergency Food", "Water Purification"],
            ContactPerson: "Ashraf Ali",
            ContactEmail: "contact@dhakatrust.example",
            ContactNumber: "+880 2 888999"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = (await createResp.Content.ReadFromJsonAsync<ApiEnvelope<NgoDto>>())!.Data!;
        Assert.Equal("Dhaka Relief Trust", created.Name);

        // Read by ID
        var getResp = await citizen.GetAsync($"{BasePath}/ngos/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        // Update NGO
        var updateResp = await citizen.PutAsJsonAsync($"{BasePath}/ngos/{created.Id}", new UpdateNgoRequest(
            Name: "Dhaka Relief & Medical Trust",
            Latitude: 23.8000,
            Longitude: 90.4000,
            FocusAreas: ["Emergency Food", "Water Purification", "Mobile Clinics"],
            ContactPerson: "Ashraf Ali",
            ContactEmail: "contact@dhakatrust.example",
            ContactNumber: "+880 2 888999"));
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = (await updateResp.Content.ReadFromJsonAsync<ApiEnvelope<NgoDto>>())!.Data!;
        Assert.Equal("Dhaka Relief & Medical Trust", updated.Name);

        // Delete NGO via Government
        var deleteResp = await gov.DeleteAsync($"{BasePath}/ngos/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getAfterDelete = await citizen.GetAsync($"{BasePath}/ngos/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task Registry_endpoints_enforce_role_based_access_control()
    {
        await ResetAsync();
        var anonymous = Client();
        var citizen = Client(Roles.Citizen);

        // Anonymous -> 401
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{BasePath}/hospitals")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{BasePath}/volunteers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{BasePath}/ngos")).StatusCode);

        // Citizen accessing read endpoints -> 200 OK
        Assert.Equal(HttpStatusCode.OK, (await citizen.GetAsync($"{BasePath}/hospitals")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await citizen.GetAsync($"{BasePath}/volunteers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await citizen.GetAsync($"{BasePath}/ngos")).StatusCode);

        // Citizen attempting Government-only actions -> 403 Forbidden
        var hospReq = new CreateHospitalRequest("Blocked", 23.8, 90.4, 50, 10, 5, 1, true, null, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await citizen.PostAsJsonAsync($"{BasePath}/hospitals", hospReq)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await citizen.PutAsJsonAsync($"{BasePath}/hospitals/{Guid.NewGuid()}", hospReq)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await citizen.DeleteAsync($"{BasePath}/hospitals/{Guid.NewGuid()}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await citizen.DeleteAsync($"{BasePath}/volunteers/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await citizen.DeleteAsync($"{BasePath}/ngos/{Guid.NewGuid()}")).StatusCode);
    }
}
