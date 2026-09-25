# F17: Safety Zones & Road Closures Specification & Implementation

## 1. Overview
Feature F17 delivers a full-stack situational hazard zoning and transport blockade management system for the RapidRelief disaster response platform. It empowers Government operators to establish emergency danger zones, evacuation perimeters, safe assembly locations, and road closures, broadcasting them in real time across situational awareness maps for citizens and rescue teams.

---

## 2. Contracts & Shared Layer (`RapidRelief.Shared.Contracts`)

### Enums
- **`ZoneType`**:
  - `DangerZone = 1`: Active hazards (floods, structural risks, fires).
  - `EvacuationZone = 2`: Compulsory evacuation perimeters.
  - `SafeAssemblyPoint = 3`: High-elevation assembly points, relief staging areas.
  - `RestrictedArea = 4`: Tactical/emergency-vehicle-only transit corridors.
- **`ClosureSeverity`**:
  - `Caution = 1`: Partial block or waterlogging (slow traffic).
  - `Impasse = 2`: Unpassable for light vehicles.
  - `TotalClosure = 3`: Completely blocked for all vehicles.
  - `Flooded = 4`: Submerged road deck / bridge approach.
  - `DebrisBlocked = 5`: Blocked by fallen structures / trees.

### Read Models & Requests
- `SafetyZoneDto` (Id, Name, Description, ZoneType, Severity, GeometryType, CenterLat, CenterLng, RadiusMeters, CoordinatesJson, IsActive, CreatedAtUtc, ExpiresAtUtc)
- `RoadClosureDto` (Id, RoadName, Description, Severity, CoordinatesJson, BlockedReason, AlternateRouteAdvice, IsActive, ReportedAtUtc, EstimatedReopenUtc)
- `CreateSafetyZoneRequest`, `UpdateSafetyZoneRequest`, `CreateRoadClosureRequest`, `UpdateRoadClosureRequest`

### Service Interface
- `ISafetyZonesReadService`:
  - `Task<IReadOnlyList<SafetyZoneDto>> GetActiveSafetyZonesAsync(CancellationToken ct = default)`
  - `Task<IReadOnlyList<RoadClosureDto>> GetActiveRoadClosuresAsync(CancellationToken ct = default)`

### Domain Events & Topics
- `SafetyZoneCreated`, `SafetyZoneUpdated`, `RoadClosureCreated`, `RoadClosureUpdated`
- Real-time topics: `safety.zones.updated`, `safety.closures.updated`

---

## 3. Backend Implementation (`RapidRelief.Api.Features.SafetyZones`)

### Database & Entity Framework Core
- **`SafetyZonesDbContext`**:
  - Table `safety_zones`: Id, Name, Description, ZoneType, Severity, GeometryType, CenterLat, CenterLng, RadiusMeters, CoordinatesJson, IsActive, CreatedAtUtc, ExpiresAtUtc.
  - Table `safety_road_closures`: Id, RoadName, Description, Severity, CoordinatesJson, BlockedReason, AlternateRouteAdvice, IsActive, ReportedAtUtc, EstimatedReopenUtc.
  - Migration History: `__efmigrationshistory_safety`
  - Migration: `20260904120000_InitialSafetyZones.cs`

### Seed Data (`SafetyZonesSeeder`)
Pre-loaded with realistic Dhaka disaster coordinates:
- *Mirpur Flood Danger Zone* (Circle, 850m radius)
- *Savar Industrial Evacuation Perimeter* (Circle, 600m radius)
- *Dhanmondi Lake Safe Assembly Point* (Circle, 350m radius)
- *Turag Basin Inundation Zone* (Custom 4-point polygon)
- *Mohakhali Emergency Response Corridor* (Restricted area)
- *Rampura Bridge Culvert Inundation* (Total closure with Hatirjheel detour)
- *Kawran Bazar Underpass Submersion* (Flooded closure with Panthapath detour)
- *Gabtoli Embankment Road Erosion* (Debris blocked with bypass)

### Endpoints (`/api/safety`)
| Method | Route | Authorization | Description |
|---|---|---|---|
| `GET` | `/api/safety/zones` | Anonymous | Get active (or all with `?all=true`) safety zones |
| `GET` | `/api/safety/zones/{id}` | Anonymous | Get safety zone details by ID |
| `POST` | `/api/safety/zones` | RequireGovernment | Create a new safety zone |
| `PUT` | `/api/safety/zones/{id}` | RequireGovernment | Update safety zone details or status |
| `DELETE` | `/api/safety/zones/{id}` | RequireGovernment | Delete safety zone |
| `GET` | `/api/safety/closures` | Anonymous | Get active (or all with `?all=true`) road closures |
| `GET` | `/api/safety/closures/{id}` | Anonymous | Get road closure details by ID |
| `POST` | `/api/safety/closures` | RequireGovernment | Report a new road closure |
| `PUT` | `/api/safety/closures/{id}` | RequireGovernment | Update road closure details or status |
| `DELETE` | `/api/safety/closures/{id}` | RequireGovernment | Remove road closure |

---

## 4. Frontend Blazor Client (`RapidRelief.Client`)

- **HTTP Client**: `SafetyZonesClient.cs` providing strongly-typed access to API endpoints.
- **Management UI**: `SafetyZonesManage.razor` accessible at `/g/safety-zones` and `/admin/safety-zones`.
  - Filterable tabs: *All Records*, *Danger & Evacuation Zones*, *Safe Assembly Points*, *Road Closures & Blockades*.
  - Modal dialogues for creating/updating zones (Circle radius or Polygon vertices) and road closures (with Detour advice).
  - Live push updates via `LiveUpdateService` subscribing to `safety.zones.updated` and `safety.closures.updated`.
- **Map Overlays**:
  - `rapidMap.js`: Native Leaflet circle (`L.circle`), polygon (`L.polygon`), and polyline (`L.polyline`) rendering with custom styling tokens and interactive popups showing detour advice and estimated reopen times.
  - `RapidMap.razor.cs`: Added `SafetyZones` and `RoadClosures` parameter collections with diffing.
  - Overlays integrated into:
    - Government Operations Map (`/g/map`)
    - Citizen Live Disaster Map (`/livemap`)
    - Rescuer Incident Mission Map (`/r/incidents/{id}`)

---

## 5. Verification & Tests
- **`SafetyZonesTests.cs`**: 13/13 passing integration tests covering:
  - Anonymous GET active zones/closures
  - Government POST/PUT/DELETE
  - Citizen / Anonymous RBAC rejection (403/401)
  - Coordinate & negative radius validation (400)
  - Degraded mode 503 fallback
- **`RapidRelief.Architecture.Tests`**: 12/12 passing with 0 module isolation or contract purity violations.
- **Build**: 0 warnings, 0 errors.
