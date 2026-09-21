# F7 Blueprint: Admin Command Center & Verification

## Goal
Provide an overview of active incidents, shelters, and system health for Admins, sourced entirely through the Contracts v1 read interfaces, with a premium dashboard UI.

## Architecture

1. **Isolation**: All code lives in `Features/CommandCenter` (Api + Client).
2. **Data Access**: F7 is an aggregation layer. It does *not* require its own `DbContext` for this scope because it sources all data from `IIncidentReadService`, `IShelterReadService`, and `IRegistryReadService`. (If future scope requires saving dashboard layouts, a `CommandCenterDbContext` can be introduced).
3. **API Endpoints**: 
   - `CommandCenterEndpoints` mapping `/api/command-center/overview`.
   - Checks `DatabaseHealth.PostgresAvailable` (D-005 degraded mode). If false, returns a `503 Service Unavailable` with `ProblemDetails`.
   - Aggregates data from the read services concurrently using `Task.WhenAll`.
   - Uses `FluentValidation` for any query parameters if added.
4. **Frontend UI**:
   - `Dashboard.razor` at `/admin/command-center`.
   - Uses `@attribute [Authorize(Roles = RapidRelief.Shared.Contracts.Enums.Roles.Admin)]` for consistency with `SheltersManage.razor`.
   - Premium dashboard layout with statistics cards and status badges.
   - Gracefully handles 503 errors (degraded mode) by displaying an appropriate offline/degraded message instead of crashing.
5. **Testing**:
   - `CommandCenterEndpointsTests` using `TestingWebAppFactory`.
   - Mock/Stub services are already provided by `Features/Stubs` and registered via DI.

## API Contracts
```csharp
public record OverviewDto(
    int TotalIncidents,
    int CriticalIncidents,
    int TotalShelters,
    int AvailableShelterCapacity,
    int TotalHospitals,
    int TotalVolunteers,
    int TotalNgos
);
```
