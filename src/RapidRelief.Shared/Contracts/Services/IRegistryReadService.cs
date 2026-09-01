using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Shared.Contracts.Services;

public interface IRegistryReadService
{
    Task<IReadOnlyList<HospitalSummaryDto>> GetHospitalsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VolunteerSummaryDto>> GetVolunteersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NgoSummaryDto>> GetNgosAsync(CancellationToken ct = default);
}
