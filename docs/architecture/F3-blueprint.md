# F3 — Shelter Management & Finder: Implementation Blueprint

## DECISIONS

Ready to paste into PROJECT-CONTEXT.md §7 (chunk B commits them):

| ID | Date | Decision | Why |
|---|---|---|---|
| D-021 | 2026-09-02 | **Shelter distance sorting:** `FakeShelterReadService`'s `HaversineMeters` function is promoted to a shared static helper (`HaversineHelper` or in `Shelter` domain) to be used by both the fake and real implementations. | Ensures identical distance math across fakes and real db (SQLite doesn't natively support PostGIS functions, so in-memory math is required for tests to run). |
| D-022 | 2026-09-02 | **AI Fallback for Shelter Recommendation:** When AI is unavailable or fails, `SheltersAiEndpoints` explicitly fetches nearest shelters using the `IShelterReadService` (real or fake) as a fallback. | Enforces rule §4.8 (demo must never depend on network). |
| D-023 | 2026-09-02 | **OpsDbContext instantiation:** Created `OpsDbContext` in `Features/Shelters/Data` as the first Ops-owned context, following the F0 Sample slice pattern. | Proven pattern; allows independent migrations (`ops_shelters`). F12 and F14 will reuse this context later. |

---

## BLUEPRINT

### 1. Packages (None needed)

No new packages needed beyond existing project dependencies.

### 2. File tree

```
src/RapidRelief.Api/Features/Shelters/
  SheltersModule.cs                      # IFeatureModule, Name="Shelters"
  Domain/Shelter.cs                      # Entity
  Domain/HaversineHelper.cs              # Math for sorting
  Data/OpsDbContext.cs                   # OpsDbContext for F3, F12, F14
  Data/Migrations/…                      # generated — InitialOps
  Endpoints/SheltersDtos.cs              # Request/Response DTOs + Validators
  Endpoints/SheltersEndpoints.cs         # Admin CRUD + Citizen finder
  Endpoints/SheltersAiEndpoints.cs       # AI recommendation with fallback
  Services/ShelterReadService.cs         # Real IShelterReadService implementation

src/RapidRelief.Client/Features/Shelters/
  SheltersClient.cs                      # API client 
  SheltersModels.cs                      # Wire DTO mirrors
  Pages/Admin/SheltersManage.razor       # Admin CRUD & occupancy
  Pages/Citizen/SheltersFinder.razor     # Citizen map + list
  Components/ShelterMapLayer.razor       # Map integration

tests/RapidRelief.Api.Tests/Shelters/
  SheltersTests.cs                       # Integration tests (Admin + Citizen)
```

### 3. Entities

```csharp
// Domain/Shelter.cs
public sealed class Shelter
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // max 100
    public GeoPoint Location { get; set; } = new(0, 0); // owned type
    public int Capacity { get; set; }
    public int CurrentOccupancy { get; set; }
    public List<string> Facilities { get; set; } = new(); // PostgreSQL JSONB or string[]
    public ShelterStatus Status { get; set; }
}

public enum ShelterStatus { Open, Full, Closed }
```

### 4. OpsDbContext

```csharp
// Data/OpsDbContext.cs
public sealed class OpsDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_ops";
    public OpsDbContext(DbContextOptions<OpsDbContext> options) : base(options) { }
    public DbSet<Shelter> Shelters => Set<Shelter>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Shelter>(s => {
            s.ToTable("ops_shelters");
            s.HasKey(x => x.Id);
            s.Property(x => x.Name).IsRequired().HasMaxLength(100);
            s.OwnsOne(x => x.Location);
        });
    }
}
```

### 5. Endpoints

- `GET /api/shelters` (Admin + Citizen): Returns paged shelters. If `lat` and `lng` provided, returns nearest sorted by Haversine distance.
- `GET /api/shelters/{id}`
- `POST /api/shelters` (Admin): Create shelter.
- `PUT /api/shelters/{id}` (Admin): Update shelter.
- `PATCH /api/shelters/{id}/occupancy` (Admin): Update occupancy explicitly.
- `GET /api/shelters/recommend` (Citizen): Consumes `IAiAnalysisService` to suggest the best shelter, falls back to `IShelterReadService.GetNearestAsync` if AI unavailable.

### 6. Client specification

- **SheltersManage.razor**: Table/list of shelters. Buttons for edit/add. Modal or inline form for updating current occupancy quickly.
- **SheltersFinder.razor**: Uses browser Geolocation API (or mocked location for demo). List of nearest shelters. Displays `<RapidMap>` with `ShelterMapLayer`. Includes deep link to Google Maps (`https://www.google.com/maps/search/?api=1&query={lat},{lng}`).

---

## IMPLEMENTATION CHUNKS

### Chunk A — Server slice (independently green)

Scope: `Features/Shelters/` backend. Entity, `OpsDbContext`, `HaversineHelper`, migration, `SheltersEndpoints`, `ShelterReadService` (replaces fake via DI), AI fallback logic. Tests in `SheltersTests.cs`. 

Verify:
- `dotnet test` (100% green, proving Fake logic is displaced cleanly and new real DB tests pass).
- Degraded mode works (returns 503 for direct DB hits).

### Chunk B — Client + docs/bookkeeping

Scope: `Features/Shelters/` frontend. Wire up API client, Admin Manage page, Citizen Finder page with RapidMap layer. PROJECT-CONTEXT.md updates.
