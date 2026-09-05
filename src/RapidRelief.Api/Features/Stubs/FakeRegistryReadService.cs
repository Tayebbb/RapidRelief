using RapidRelief.Api.Infrastructure.SeedData;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Stubs;

/// <summary>Returns hospitals/volunteers/NGOs from <see cref="DhakaSeedData"/> (blueprint B4).</summary>
public sealed class FakeRegistryReadService : IRegistryReadService
{
    public Task<IReadOnlyList<HospitalSummaryDto>> GetHospitalsAsync(CancellationToken ct = default)
        => Task.FromResult(DhakaSeedData.Hospitals);

    public Task<IReadOnlyList<VolunteerSummaryDto>> GetVolunteersAsync(CancellationToken ct = default)
        => Task.FromResult(DhakaSeedData.Volunteers);

    public Task<IReadOnlyList<NgoSummaryDto>> GetNgosAsync(CancellationToken ct = default)
        => Task.FromResult(DhakaSeedData.Ngos);
}
