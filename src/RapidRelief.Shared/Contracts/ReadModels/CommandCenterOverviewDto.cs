namespace RapidRelief.Shared.Contracts.ReadModels;

public record CommandCenterOverviewDto(
    int TotalActiveIncidents,
    int TotalCriticalIncidents,
    int TotalOpenShelters,
    int TotalShelterCapacity,
    int TotalHospitals,
    int TotalVolunteers,
    int TotalNgos
);
