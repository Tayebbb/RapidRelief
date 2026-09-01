All context absorbed (PROJECT-CONTEXT §4/§5/§6, plan §1.1–1.2, §1.5, §2 F0 card, §8, §9). Blueprint follows.

---

# DECISIONS

Ready to paste into PROJECT-CONTEXT.md §7:

| ID | Verdict | Rationale (1-line) |
|---|---|---|
| **D-004 (A)** | **Stay on .NET 8** | D-001 froze the stack; all package pins are validated for 8.x; EOL (2026-11-10) is irrelevant for a graded local demo ending ~Week 13, while migrating means 4 SDK installs + redoing all research under deadline. |
| **D-005 (B)** | **docker-compose is the documented primary DB path; Neon/Supabase free tier is the documented fallback (override via `ConnectionStrings__Postgres` env var or user-secrets); startup is warn-and-continue-degraded** — `MigrateAsync` retries 3× then logs a prominent warning, sets `DatabaseHealth.PostgresAvailable=false`, app keeps serving (stub-backed pages work; DB-backed endpoints return 503 ProblemDetails; `/health` reports it) | Consistent with rule §4.8 ("demo must never depend on network"); a dev with no Docker/Postgres still runs the app against stubs, and all F0 tests are provable via the SQLite factory alone. |
| **D-006 (C)** | **Hand-rolled in-process event bus (~50 lines) instead of MediatR notifications** (deviation from plan §8.6) | MediatR is commercial from v13 (accidental `dotnet add package` = license risk for students); we need only pub/sub notifications, and a zero-dependency bus with per-handler try/catch gives exactly the plan's "missing subscriber breaks nothing" semantics. |
| **D-007 (D)** | **F0 ships the per-context infrastructure pattern + exactly ONE concrete context (`SampleDbContext`)**; `AuthDbContext`/`IncidentsDbContext`/`OpsDbContext`/`ReliefDbContext`/`AiDbContext` arrive with their owning features copying the proven pattern; consequently ASP.NET Identity + seeded Identity users defer to F1's first PR (FakeAuth covers all 4 roles until then) | Empty contexts are ceremony that forces Tayeb to scaffold inside teammates' lanes (violates §4.7); one real context proves history-table naming, `feature_` prefix, `--context`/`--output-dir` usage, and startup migration orchestration — everything owners need to copy. |
| **D-008 (E)** | **Sample slice = `Features/Sample` "Ping"**: `POST /api/sample/pings` (Admin policy, FluentValidation) persists `Ping` to `sample_pings` via `SampleDbContext`, publishes `PingCreated` contract event consumed by a logging handler in the same slice; `GET /api/sample/pings` (anonymous, paged envelope); Blazor page `/sample` posts+lists via the dev-role header; full integration test via SQLite factory | One tiny slice exercises every foundation mechanism (module self-registration, per-context migrations, envelope, validation, auth policy + FakeAuth, event bus, client page, test factory) — the literal copy-me template plan §8.1 demands. |

---

# BLUEPRINT

## B1. Target file tree

```
RapidRelief.sln
global.json                          # { "sdk": { "version": "8.0.100", "rollForward": "latestFeature" } }
Directory.Build.props                # net8.0, Nullable+ImplicitUsings enable, LangVersion 12
.config/dotnet-tools.json            # dotnet-ef 8.0.30 (local tool)
.gitignore                           # VS defaults + App_Data/uploads; must NOT exclude wwwroot/lib
docker-compose.yml
.env.example
README.md                            # + run guide (updated)
PROJECT-CONTEXT.md                   # updated every chunk (mandatory)
docs/
  api-conventions.md                 # plan §8.9 one-pager: routes, envelope, paging, errors, DTO naming
  event-bus.md                       # plan §8.6 one-page how-to
.github/
  workflows/ci.yml
  pull_request_template.md           # DoD checklist
  CODEOWNERS                         # /src/**/Features/<X>/ → owner handle (placeholders)
src/
  RapidRelief.Shared/                # ZERO package references — contracts only
    RapidRelief.Shared.csproj
    Contracts/
      Common/GeoPoint.cs  PagedResult.cs  ApiEnvelope.cs
      Enums/DisasterType.cs Severity.cs IncidentStatus.cs MissionStatus.cs
            ReliefStatus.cs ResourceType.cs Roles.cs
      Eventing/IEvent.cs EventBase.cs IEventHandler.cs IEventBus.cs
      Events/IncidentCreated.cs IncidentAssessed.cs IncidentVerified.cs
             MissionAssigned.cs MissionStatusChanged.cs ReliefRequested.cs
             ReliefStatusChanged.cs AlertPublished.cs AuthEvent.cs PingCreated.cs
      ReadModels/IncidentSummaryDto.cs IncidentQuery.cs ShelterSummaryDto.cs
                 HospitalSummaryDto.cs VolunteerSummaryDto.cs NgoSummaryDto.cs
                 UserSummaryDto.cs AiAnalysisRequest.cs AiAssessmentDto.cs StoredFile.cs
      Services/IIncidentReadService.cs IShelterReadService.cs IRegistryReadService.cs
               IUserAdminService.cs IAiAnalysisService.cs IRealtimeNotifier.cs IFileStorage.cs
  RapidRelief.Api/
    RapidRelief.Api.csproj           # refs: Shared, Client + pkgs (see B8)
    Program.cs
    appsettings.json  appsettings.Development.json  Properties/launchSettings.json
    Infrastructure/
      Modules/IFeatureModule.cs  ModuleDiscovery.cs
      Eventing/InProcessEventBus.cs
      Auth/FakeAuthHandler.cs  AuthSetup.cs  AuthPolicies.cs
      Persistence/MigrationRunner.cs  DatabaseHealth.cs
      Http/EndpointResults.cs        # envelope + ValidationProblem helpers
      Storage/LocalDiskFileStorage.cs
    Features/
      Foundation/FoundationModule.cs # maps /api/foundation/whoami, /health
      Sample/
        SampleModule.cs
        Domain/Ping.cs
        Data/SampleDbContext.cs
        Data/Migrations/…            # generated: --context SampleDbContext --output-dir Features/Sample/Data/Migrations
        Endpoints/PingEndpoints.cs   # + CreatePingRequest, CreatePingValidator, PingDto
        Handlers/PingCreatedLoggingHandler.cs
      Ai/AiModule.cs  RuleBasedAiAnalysisService.cs      # permanent fallback; F8 grows here (Tayeb's lane)
      Realtime/RealtimeModule.cs  NoOpRealtimeNotifier.cs # F9 grows here (Tayeb's lane)
      Stubs/
        StubsModule.cs               # Order = int.MaxValue, registers via TryAdd*
        FakeIncidentReadService.cs  FakeShelterReadService.cs
        FakeRegistryReadService.cs  FakeUserAdminService.cs
        SeedData/DhakaSeedData.cs    # the single deterministic dataset
  RapidRelief.Client/                # dotnet new blazorwasm --pwa
    RapidRelief.Client.csproj        # refs: Shared
    Program.cs  App.razor  _Imports.razor
    Layout/MainLayout.razor  NavMenu.razor
    Common/
      Map/RapidMap.razor  RapidMap.razor.cs  MapMarker.cs
      Auth/DevRoleState.cs  DevRoleHandler.cs  DevRolePicker.razor  # Development only
    Features/Sample/SamplePage.razor # @page "/sample"
    wwwroot/
      index.html  manifest.json  service-worker.js  service-worker.published.js  icon-*.png
      css/app.css
      js/rapidMap.js
      lib/leaflet/leaflet.js  leaflet.css  images/   # Leaflet 1.9.4 committed locally — NO CDN
tests/
  RapidRelief.Api.Tests/
    RapidRelief.Api.Tests.csproj
    TestingWebAppFactory.cs          # env "Testing", SQLite :memory: per context, EnsureCreated
    Eventing/InProcessEventBusTests.cs
    Auth/FakeAuthTests.cs
    Sample/SamplePingTests.cs
    Stubs/StubDataTests.cs  RuleBasedAiTests.cs
    Smoke/DiResolutionSmokeTests.cs
  RapidRelief.Architecture.Tests/
    RapidRelief.Architecture.Tests.csproj
    ModuleIsolationTests.cs  ContractsPurityTests.cs  DbContextOwnershipTests.cs
```

## B2. Contracts v1 — exact C# signatures

Everything below lives in `RapidRelief.Shared/Contracts` (namespace `RapidRelief.Shared.Contracts.*`), data-only, zero package refs. **Scope is exactly PROJECT-CONTEXT §6 + `IEventBus` + `PingCreated`** — nothing invented (e.g., no `ITeamReadService`; that's a workshop-time additive change).

```csharp
// ─── Common ───
public sealed record GeoPoint(double Latitude, double Longitude);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record ApiEnvelope<T>(T Data);   // success only; errors are always RFC7807 ProblemDetails

// ─── Enums ───
public enum DisasterType { Flood, Earthquake, Fire, Cyclone, Landslide, BuildingCollapse, Other }
public enum Severity { Minimal = 1, Minor = 2, Moderate = 3, Severe = 4, Catastrophic = 5 }
public enum IncidentStatus { Reported, Verified, Assigned, InProgress, Resolved, Rejected }
public enum MissionStatus { Assigned, EnRoute, OnScene, Completed, Cancelled }
public enum ReliefStatus { Pending, Approved, Rejected, Allocated, Dispatched, Delivered }
public enum ResourceType { Food, Water, Medicine, Shelter, Clothing, Other }
public static class Roles
{
    public const string Citizen = "Citizen"; public const string Rescue = "Rescue";
    public const string Admin = "Admin";     public const string Ngo = "NGO";
    public static readonly IReadOnlyList<string> All = new[] { Citizen, Rescue, Admin, Ngo };
}

// ─── Eventing ───
public interface IEvent { Guid EventId { get; } DateTimeOffset OccurredAtUtc { get; } }
public abstract record EventBase : IEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent evt, CancellationToken ct = default);
}
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : IEvent;
}

// ─── Events (all `: EventBase`) ───
public sealed record IncidentCreated(Guid IncidentId, Guid ReporterUserId, DisasterType Type,
    Severity ReportedSeverity, GeoPoint Location, string Description, bool IsSos,
    IReadOnlyList<string> PhotoPaths) : EventBase;
public sealed record IncidentAssessed(Guid IncidentId, Severity EstimatedSeverity,
    double PriorityScore, string Summary, Guid? PossibleDuplicateOfId) : EventBase;
public sealed record IncidentVerified(Guid IncidentId, Guid VerifiedByUserId, bool Approved, string? Reason) : EventBase;
public sealed record MissionAssigned(Guid MissionId, Guid IncidentId, Guid TeamId, Guid AssignedByUserId) : EventBase;
public sealed record MissionStatusChanged(Guid MissionId, Guid IncidentId, MissionStatus NewStatus) : EventBase;
public sealed record ReliefRequested(Guid RequestId, Guid RequesterUserId, ResourceType Type,
    int Quantity, GeoPoint Location, int UrgencyLevel) : EventBase;
public sealed record ReliefStatusChanged(Guid RequestId, ReliefStatus NewStatus) : EventBase;
public sealed record AlertPublished(Guid AlertId, string Title, string Body, Severity Severity,
    DisasterType? Type, DateTimeOffset ExpiresAtUtc) : EventBase;
public sealed record AuthEvent(Guid UserId, string Action, string? Details) : EventBase; // "Login","Lock","RoleChange",…
public sealed record PingCreated(Guid PingId, string Message) : EventBase;               // sample-slice demo event

// ─── Read models / DTOs ───
public sealed record IncidentSummaryDto(Guid Id, DisasterType Type, Severity Severity,
    IncidentStatus Status, GeoPoint Location, string Summary, DateTimeOffset ReportedAtUtc,
    bool IsSos, double? PriorityScore);
public sealed record IncidentQuery(IncidentStatus? Status = null, DisasterType? Type = null,
    Severity? MinSeverity = null, int Page = 1, int PageSize = 50);
public sealed record ShelterSummaryDto(Guid Id, string Name, GeoPoint Location,
    int Capacity, int Occupancy, bool IsOpen);
public sealed record HospitalSummaryDto(Guid Id, string Name, GeoPoint Location,
    int TotalBeds, int AvailableBeds, IReadOnlyList<string> Specialties);
public sealed record VolunteerSummaryDto(Guid Id, string Name, IReadOnlyList<string> Skills,
    bool IsAvailable, GeoPoint? Location);
public sealed record NgoSummaryDto(Guid Id, string Name, IReadOnlyList<string> FocusAreas, string ContactEmail);
public sealed record UserSummaryDto(Guid Id, string Email, string DisplayName,
    IReadOnlyList<string> Roles, bool IsLocked);
public sealed record AiAnalysisRequest(Guid IncidentId, DisasterType ReportedType, string Description,
    GeoPoint Location, bool IsSos, DateTimeOffset ReportedAtUtc, IReadOnlyList<string> PhotoPaths);
public sealed record AiAssessmentDto(Guid IncidentId, DisasterType PredictedType, Severity EstimatedSeverity,
    double PriorityScore, string Summary, Guid? PossibleDuplicateOfId, string Provider); // Provider: "RuleBased"|"Gemini"
public sealed record StoredFile(string Path, string Url, long SizeBytes, string ContentType);

// ─── Service interfaces ───
public interface IIncidentReadService
{
    Task<PagedResult<IncidentSummaryDto>> GetIncidentsAsync(IncidentQuery query, CancellationToken ct = default);
    Task<IncidentSummaryDto?> GetByIdAsync(Guid incidentId, CancellationToken ct = default);
}
public interface IShelterReadService
{
    Task<IReadOnlyList<ShelterSummaryDto>> GetSheltersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ShelterSummaryDto>> GetNearestAsync(GeoPoint origin, int count = 5, CancellationToken ct = default);
}
public interface IRegistryReadService
{
    Task<IReadOnlyList<HospitalSummaryDto>> GetHospitalsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VolunteerSummaryDto>> GetVolunteersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NgoSummaryDto>> GetNgosAsync(CancellationToken ct = default);
}
public interface IUserAdminService
{
    Task<PagedResult<UserSummaryDto>> GetUsersAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<bool> SetLockedAsync(Guid userId, bool locked, CancellationToken ct = default);
    Task<bool> SetRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default);
}
public interface IAiAnalysisService
{
    Task<AiAssessmentDto> AnalyzeIncidentAsync(AiAnalysisRequest request, CancellationToken ct = default);
}
public interface IRealtimeNotifier
{
    Task NotifyAllAsync(string topic, object payload, CancellationToken ct = default);
    Task NotifyRoleAsync(string role, string topic, object payload, CancellationToken ct = default);
    Task NotifyUserAsync(Guid userId, string topic, object payload, CancellationToken ct = default);
}
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
}
```

## B3. Event bus behavior spec (`InProcessEventBus`)

- Registered **scoped** (critical: resolves scoped handlers like future DbContext-using subscribers correctly); ctor takes `IServiceProvider` (ambient scope) + `ILogger<InProcessEventBus>`.
- `PublishAsync<TEvent>`: `GetServices<IEventHandler<TEvent>>()`, await each **sequentially**, each wrapped in try/catch → `LogError(ex, "Handler {Handler} failed for {Event} {EventId}")` and **continue**. Zero handlers = silent success. Publisher never throws because of a handler.
- "Fire-and-forget" is at the *module* level (publisher unaffected by subscriber failure), **not** thread level — no `Task.Run` (avoids disposed-scope bugs with scoped handlers).
- Handlers register per-feature in their own module: `services.AddScoped<IEventHandler<PingCreated>, PingCreatedLoggingHandler>()`.

## B4. Stub behavior specs

All fakes are **deterministic** (hard-coded arrays with fixed GUIDs — no `Random`), registered as **singletons** in `StubsModule` via `TryAdd*` so any real registration wins (see B5). Dataset: `DhakaSeedData` (demo city: **Dhaka, center 23.8103, 90.4125**).

| Stub | Behavior |
|---|---|
| `DhakaSeedData` | Static readonly collections: **28 incidents** across Mirpur, Uttara, Gulshan, Mohammadpur, Lalbagh/Old Dhaka, Motijheel, Dhanmondi, Badda, Khilgaon, Tejgaon, Savar, Keraniganj — Flood-heavy mix (monsoon realism) + Fire (garment district), BuildingCollapse (Savar), Cyclone-fringe, Landslide, Other; all severities 1–5; statuses spread across the state machine; **≥3 SOS**; **one intentional near-duplicate pair** (same block, same type, 20 min apart — F8's duplicate demo); `ReportedAtUtc` spread over the trailing 72 h *relative to a fixed anchor date* (deterministic). Also **8 shelters** (schools/colleges as cyclone shelters; 1 at full capacity, 1 `IsOpen=false`), **6 hospitals**, **10 volunteers**, **5 NGOs**, and **6 rescue teams** (teams have no contract surface yet — data ships in the seed class for F5/F6 to consume when `ITeamReadService` is added additively at the workshop). |
| `FakeIncidentReadService` | Filters/pages `DhakaSeedData.Incidents` per `IncidentQuery`; correct `TotalCount`. |
| `FakeShelterReadService` | Returns the 8 shelters; `GetNearestAsync` = Haversine sort, take `count`. |
| `FakeRegistryReadService` | Returns hospitals/volunteers/NGOs from seed. |
| `FakeUserAdminService` | In-memory list of 4 users matching §5 seeded identities (`citizen1@rr.dev` etc., fixed GUIDs — the same GUIDs FakeAuth issues); `SetLockedAsync`/`SetRolesAsync` mutate in-memory, return `false` for unknown id. |
| `RuleBasedAiAnalysisService` (in `Features/Ai` — permanent fallback, Tayeb's lane) | Pure function: type = keyword match over `Description` (flood/fire/earthquake/collapse word lists) else `ReportedType`; severity = reported, +1 (clamped ≤5) if any of "trapped", "children", "spreading", "injured"; priority = `20×severity + (IsSos?25:0) + recency bonus 0–15 (age<6h)`, clamp 0–100; summary = one-sentence template; `PossibleDuplicateOfId = null` (geo logic is F8); `Provider = "RuleBased"`. |
| `NoOpRealtimeNotifier` (in `Features/Realtime`) | Logs at Debug, returns `Task.CompletedTask`. |
| `LocalDiskFileStorage` (Infrastructure) | Writes to `{ContentRoot}/App_Data/uploads/{yyyy-MM}/{newGuid}{sanitized ext}` — **never trusts the client filename** (extension whitelist-sanitized; original name discarded); `Url` = relative storage path (public serving is F2's decision); `OpenReadAsync` returns null for missing/path-escaping input (reject `..`). Root configurable `FileStorage:Root`. |
| `FakeAuthHandler` (Infrastructure/Auth) | Scheme `"FakeAuth"`, registered **only** when env is Development **or Testing**. Reads `X-Dev-Role`; value ∈ `Roles.All` (case-insensitive) → success principal: `ClaimTypes.Role` = role, `NameIdentifier` = that role's fixed seed GUID, `Name` = `{role.ToLower()}1@rr.dev`; header absent → `NoResult`; invalid value → `Fail`. |

**Auth composition (finding 8):** default scheme = policy scheme `"MultiAuth"` whose `ForwardDefaultSelector` returns `"FakeAuth"` iff `X-Dev-Role` header present **and** FakeAuth is enabled (Dev/Testing), else `JwtBearerDefaults.AuthenticationScheme`. JwtBearer configured from `Jwt:Issuer/Audience/SigningKey` incl. the SignalR `access_token` `OnMessageReceived` hook (cheap now, F9-ready). Policies in `AuthPolicies`: `RequireAdmin/RequireRescue/RequireCitizen/RequireNgo` = `RequireRole(...)` only — **no scheme names in policies**.

## B5. Module self-registration pattern

```csharp
public interface IFeatureModule   // Api/Infrastructure/Modules — server-only, NOT in Shared
{
    string Name { get; }
    int Order => 0;                                   // Stubs overrides with int.MaxValue
    void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
    Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct) => Task.CompletedTask;
}
```

- `ModuleDiscovery`: reflection over `typeof(Program).Assembly` for non-abstract `IFeatureModule` with public parameterless ctor → instantiate → **sort by `Order`, then `Name`** (deterministic).
- **Stub-yield rule:** feature modules register real services with plain `Add*`; `StubsModule` (`Order = int.MaxValue`, runs last) uses `TryAdd*` — so a real implementation automatically displaces its fake the moment its module registers it, and the fake silently resumes if the real module is ever pulled. This mechanizes rule §4.5.
- Each context-owning module implements `MigrateAsync` for **its own context only** (e.g., `SampleModule` → `SampleDbContext.Database.MigrateAsync(ct)`).

## B6. `Program.cs` composition order (exact)

```text
 1. Serilog bootstrap logger → builder.Host.UseSerilog(from config)
 2. AddProblemDetails + AddExceptionHandler (shared framework — no packages)
 3. AddRateLimiter: global fixed-window per-IP (generous, e.g. 100/10s) + named "auth", "reports"
    policies (skeleton); RejectionStatusCode 429; entirely skipped when env == Testing
 4. AddValidatorsFromAssemblyContaining<Program>()   // FluentValidation, EXPLICIT validation only
 5. AuthSetup: AddAuthentication("MultiAuth") → policy scheme + JwtBearer + FakeAuth (Dev/Testing);
    AddAuthorization + AuthPolicies
 6. services.AddScoped<IEventBus, InProcessEventBus>()
 7. services.AddSingleton<IFileStorage, LocalDiskFileStorage>() + DatabaseHealth singleton
 8. var modules = ModuleDiscovery.Discover(); foreach → AddModule(services, config, env)
    // SampleModule adds DbContext: UseNpgsql(cs, o => o.MigrationsHistoryTable("__efmigrationshistory_sample"))
    //   — guarded: only when env != Testing (test factory injects SQLite options itself)
 9. build → UseExceptionHandler + UseStatusCodePages(ProblemDetails)
10. UseSerilogRequestLogging
11. UseBlazorFrameworkFiles + UseStaticFiles
12. UseRateLimiter (skip in Testing)
13. UseAuthentication → UseAuthorization
14. foreach module → MapEndpoints(app)      // each maps its own /api/{feature} group
15. MapFallbackToFile("index.html")
16. if env != Testing: MigrationRunner.RunAsync(app, modules)
    // per module: try MigrateAsync ×3 (2s backoff) → on total failure LogError + DatabaseHealth=false; NEVER crash
17. app.Run()   // + public partial class Program { } for WebApplicationFactory
```

## B7. Config, compose, CI, PWA

**appsettings.json** (committed, no secrets): `Serilog` (console sink, Information), `ConnectionStrings:Postgres` = `""`, `Jwt: { Issuer: "RapidRelief", Audience: "RapidRelief", SigningKey: "" }`, `FileStorage:Root: "App_Data/uploads"`, `RateLimiting` knobs.
**appsettings.Development.json**: Postgres cs `Host=localhost;Port=5432;Database=rapidrelief;Username=rapidrelief;Password=rapidrelief_dev`, a dev-only signing key (≥32 chars, comment-marked DEV ONLY), Serilog Debug. Cloud-Postgres fallback: override via user-secrets or `ConnectionStrings__Postgres` env var (documented in README). Gemini keys: user-secrets, F8's problem.

**docker-compose.yml**

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-rapidrelief}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-rapidrelief_dev}
      POSTGRES_DB: ${POSTGRES_DB:-rapidrelief}
    ports: ["${POSTGRES_PORT:-5432}:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck: { test: ["CMD-SHELL", "pg_isready -U rapidrelief"], interval: 5s, retries: 10 }
volumes: { pgdata: }
```

**.env.example**: `POSTGRES_USER=rapidrelief`, `POSTGRES_PASSWORD=rapidrelief_dev`, `POSTGRES_DB=rapidrelief`, `POSTGRES_PORT=5432`.

**CI (.github/workflows/ci.yml)** — trigger: PR + push to main.
- Job `build-test` (ubuntu): `actions/setup-dotnet` 8.0.x → `dotnet tool restore` → `dotnet restore` → `dotnet build -c Release --no-restore` → `dotnet test -c Release --no-build` (SQLite/unit/arch — no services).
- Job `postgres-fidelity` (after build-test): `services: postgres:16` (user/pass/db as above + health options) → `dotnet tool restore` → `dotnet ef database update --project src/RapidRelief.Api --context SampleDbContext` with `ConnectionStrings__Postgres` env — proves migrations are valid Npgsql SQL. Extend with one context per future feature.

**PWA notes:** keep template `service-worker.js` (dev = pass-through; offline only via `dotnet publish` using `service-worker.published.js`) — do not chase offline in dev. Leaflet 1.9.4 committed under `wwwroot/lib/leaflet` (UMD via `<script>` in index.html — global `L`; `rapidMap.js` is the ES module our code imports via `IJSRuntime` in `OnAfterRenderAsync(firstRender)`); offline map shell works, OSM tiles need network (accepted). `RapidMap` v1 = init/setView/upsertMarkers(diff by id)/removeMarkers/click-to-pin/dispose + `IAsyncDisposable`; polygons/heat-layer are documented extension points added later to the wrapper API (foundation-owned file — features never edit internals, per plan §8.8).

## B8. Package pins (per research — never deviate)

Api: `Microsoft.EntityFrameworkCore` + `.Design` (PrivateAssets) 8.0.30, `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11, `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.30, `Microsoft.AspNetCore.Components.WebAssembly.Server` 8.0.30, `FluentValidation.DependencyInjectionExtensions` 12.1.1 (**never** FluentValidation.AspNetCore), `Serilog.AspNetCore` 9.0.0. Client: `Microsoft.AspNetCore.Components.WebAssembly(.DevServer)` 8.0.30. Tests: `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.12.0, `Microsoft.AspNetCore.Mvc.Testing` 8.0.30, `Microsoft.EntityFrameworkCore.Sqlite` 8.0.30, `NetArchTest.eNhancedEdition` 1.4.5. Shared: **zero**. Tools: `dotnet-ef` 8.0.30 local. Deferred: Identity.EntityFrameworkCore → F1; SignalR.Client → F9; rate limiting + ProblemDetails = shared framework, no packages.

---

# IMPLEMENTATION CHUNKS

### Chunk 1 — Skeleton, contracts, bus, modules, auth, middleware, arch tests, CI
Scaffold everything in B1 except: Sample slice persistence, stubs, RapidMap/Leaflet, docs. Includes: `global.json`, props, sln, 3 projects (hosted-manual per finding 2) + 2 test projects, tools manifest, full `Shared/Contracts` v1 (B2), `InProcessEventBus` (B3), `IFeatureModule` + discovery + stub-yield rule (B5), ProblemDetails/Serilog/rate-limiter/FluentValidation wiring, complete auth composition (B4 last row), `FoundationModule` with `/api/foundation/whoami` (`[Authorize]`) and `/health` (static-ok for now), bare Client app + nav, minimal `ci.yml` (build-test job), PROJECT-CONTEXT update.
**Verify:** `dotnet build` clean → `dotnet test` green (bus unit tests, FakeAuth integration tests via factory — no DB involved, arch tests) → `dotnet run --project src/RapidRelief.Api` → `/` serves Blazor, `curl /api/foundation/whoami` → 401; with `X-Dev-Role: Admin` → 200 + roles.

### Chunk 2 — EF/Postgres pattern, Sample slice end-to-end, SQLite test factory, degraded startup
`SampleDbContext` (`feature_` prefix ⇒ table `sample_pings`, history table `__efmigrationshistory_sample`), initial migration (`dotnet ef migrations add Initial --context SampleDbContext --output-dir Features/Sample/Data/Migrations` — design-time, needs no live DB), `MigrationRunner` + `DatabaseHealth` + degraded mode (D-005), `/health` now reports DB state, docker-compose + .env.example, full Ping slice (D-008: entity, validator, endpoints with envelope/ProblemDetails/Admin policy, `PingCreated` publish, logging handler, Blazor `/sample` page posting with dev-role header), `TestingWebAppFactory` (own open `SqliteConnection(":memory:")`, `EnsureCreated`, Testing env skips Npgsql registration + migration runner + rate limiter), sample integration tests, CI `postgres-fidelity` job, PROJECT-CONTEXT update.
**Verify:** `dotnet ef migrations list --context SampleDbContext` shows Initial → `dotnet test` green (all of chunk 1 + Ping tests, **no Postgres present**) → `dotnet run` with no DB: starts, logs degraded warning, `/health` shows `dbConnected:false`, `/sample` page loads, POST returns 503 ProblemDetails; (on a Docker machine/CI: compose up → ping round-trips).

### Chunk 3 — Stubs + Dhaka seed, RapidMap, dev-role picker, docs, run guide
`DhakaSeedData` + 4 fakes + `StubsModule` (TryAdd, Order max), `RuleBasedAiAnalysisService` + `AiModule`, `NoOpRealtimeNotifier` + `RealtimeModule`, `LocalDiskFileStorage`, stub/DI-smoke/rule-based-AI tests, Leaflet 1.9.4 local + `rapidMap.js` + `RapidMap` component (markers for seeded incidents shown on the `/sample` page as the wrapper's proof), `DevRolePicker` (Development-only dropdown → `X-Dev-Role` DelegatingHandler), `docs/api-conventions.md`, `docs/event-bus.md`, README run guide (compose path + Neon/Supabase fallback + no-DB degraded mode), CODEOWNERS + PR template, final PROJECT-CONTEXT update (F0 row → MVP DONE/DONE, changelog, D-004…D-008, §6 registry frozen signatures, §2 table).
**Verify:** `dotnet test` fully green → `dotnet run` → `/sample` shows Dhaka map with ≥25 seeded incident markers via `IIncidentReadService` (stub), role picker toggles 401/403/201 on ping POST → `dotnet publish -c Release` succeeds (PWA assets emitted).

---

# TEST PLAN

**Architecture tests** (`RapidRelief.Architecture.Tests`, NetArchTest.eNhancedEdition):
1. For every pair of distinct `RapidRelief.Api.Features.X` / `...Features.Y` namespaces (enumerated dynamically): types in X have no dependency on Y.
2. Types in `RapidRelief.Shared` depend on nothing outside `System.*`/`RapidRelief.Shared` (+ assert `Shared` assembly has zero non-framework references).
3. Every `IEvent` implementor resides in `RapidRelief.Shared.Contracts.Events`.
4. Every `DbContext` subclass resides in a `RapidRelief.Api.Features.*.Data` namespace.
5. Types in `RapidRelief.Api.Infrastructure` have no dependency on `RapidRelief.Api.Features.*` (discovery is reflection-only).

**Event bus unit tests:** all N handlers invoked; zero handlers → completes; first handler throws → second still runs and error logged (assert via probe handler + captured logger); scoped handler resolution works; sequential ordering preserved.

**FakeAuth integration tests** (factory, env Testing): `whoami` no header → 401; `X-Dev-Role: Admin` → 200 with Admin role claim; bogus role → 401; `POST /api/sample/pings` as Citizen → 403 (policy), as Admin → 201.

**Sample slice integration tests** (SQLite factory): POST valid → 201 + `ApiEnvelope` + Location; GET returns persisted ping with paging envelope; POST empty/overlong message → 400 ProblemDetails with field errors; `PingCreated` received by a test-registered `IEventHandler<PingCreated>` probe.

**DI smoke test:** factory boots; from a scope resolve all seven §6 interfaces + `IEventBus`; assert stub concrete types (`FakeIncidentReadService` etc.) — proves TryAdd ordering.

**Stub data tests:** incidents ≥25, all within Dhaka bounding box (lat 23.6–24.0, lon 90.2–90.6), ≥3 SOS, near-duplicate pair present; shelters == 8, `GetNearestAsync` distance-ordered, one full + one closed; `RuleBasedAi` deterministic (same input ⇒ same output), SOS outranks identical non-SOS, priority ∈ [0,100].

---

# DOD CHECKLIST (maps to plan §8)

- [ ] §8.1 Solution: 3 src + 2 test projects build from fresh clone with only .NET 8 SDK; Sample slice = working copy-me template.
- [ ] §8.2 DB: compose file + `.env.example`; `SampleDbContext` proves per-context history table + `feature_` prefix + `--context` workflow; degraded startup when Postgres absent (D-005); Neon/Supabase override documented.
- [ ] §8.3 Auth: MultiAuth policy scheme + JwtBearer plumbing + FakeAuth (Dev/Testing only) + role policies; `whoami` demonstrates it. (Identity + seeded users deferred to F1 per D-007 — recorded.)
- [ ] §8.4 Contracts v1 in `Shared/Contracts` exactly per B2; PROJECT-CONTEXT §6 updated with frozen signatures pending workshop ratification.
- [ ] §8.5 Stubs: 4 fakes + rule-based AI + no-op notifier + local storage, all DI-resolvable; Dhaka seed ≥25 incidents/8 shelters/teams.
- [ ] §8.6 Event bus + `docs/event-bus.md`; `PingCreated` flows publisher→handler in tests.
- [ ] §8.7 ProblemDetails everywhere (incl. validation 400s + 503 degraded), Serilog request logging, FluentValidation explicit pattern shown in slice, rate-limiter skeleton with named policies.
- [ ] §8.8 `RapidMap` renders seeded markers; Leaflet local; no CDN anywhere in index.html.
- [ ] §8.9 `docs/api-conventions.md` (route pattern, envelope, paging, error shape, DTO naming).
- [ ] §8.10 CI: build-test job green on PR; postgres-fidelity job applies migrations against `postgres:16`; PR template + CODEOWNERS.
- [ ] §8.11 appsettings layering + user-secrets note + `.env.example`.
- [ ] All tests in TEST PLAN green via `dotnet test` on a machine with **no Docker/Postgres**.
- [ ] PROJECT-CONTEXT.md updated in every chunk (status row, changelog, D-004…D-008, §2, §6) — per AGENTS.md this gates "done".
- [ ] Team action (not implementer-verifiable): 4/4 devs run it locally; workshop ratifies Contracts v1.

---

# RISKS (for the implementer)

1. **Hosted-Blazor manual composition** — wrong middleware order (`UseBlazorFrameworkFiles`/`MapFallbackToFile`) or missing `Components.WebAssembly.Server` ref breaks client serving; follow B6 order literally and smoke-test `/` after chunk 1.
2. **Testing-env leakage** — if Npgsql registration, `MigrationRunner`, or rate limiter run under env "Testing", tests fail mysteriously (connection errors/429s); every guard in B6 steps 3/8/12/16 must check the environment.
3. **FakeAuth not enabled in Testing** — forgetting the "Dev **or Testing**" condition (handler registration *and* forward selector) yields a 401 cascade in integration tests.
4. **SQLite vs Npgsql fidelity** — `EnsureCreated` bypasses migrations; keep the Ping model to portable types (Guid/string/timestamptz) and no Npgsql-specific model config in F0; future provider-specific config must be gated (`Database.IsNpgsql()`) — note this in api-conventions.
5. **Event bus scoping** — registering the bus as singleton while resolving scoped handlers is the classic bug; it must be scoped (B3).
6. **MediatR reflex** — agents/students habitually `dotnet add package MediatR`; D-006 forbids it — the bus is hand-rolled, package list is closed (B8).
7. **Stub-yield ordering** — if `StubsModule` registers before feature modules or uses `Add*` instead of `TryAdd*`, real implementations get shadowed later; the DI smoke test + `Order = int.MaxValue` convention protect this.
8. **Leaflet via CDN sneaking in** — index.html must reference only `lib/leaflet/*`; a CDN link silently breaks the PWA/offline story and the no-network demo rule.
9. **Migration commands without `--context`** — with multiple contexts arriving in Week 2, any `dotnet ef` invocation lacking `--context`/`--output-dir` corrupts folder ownership; api-conventions must show the exact command line (proven by the Sample slice).
10. **EOL-date confusion** — .NET 8 EOL (2026-11-10) lands before the final demo; D-004 accepts this consciously — nobody should "helpfully" upgrade mid-semester.

Open items I could not resolve from evidence (flagging, not deciding): the other 3 machines' SDK state (D-004 assumes 8.x installable/installed — verify at workshop), and final contract ratification is a team act (plan §8 "Ownership") — the B2 signatures are the proposed freeze, listed in §6 as such.