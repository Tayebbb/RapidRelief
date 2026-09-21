# Prompts for Tanjim's Remaining Tasks

Use these detailed prompts to start the implementation of each of your remaining features in the RapidRelief project. You can copy and paste these to the AI assistant when you are ready to work on that specific task.

---

## F7: Admin Command Center & Verification

**Prompt:**
```text
I need to implement the "F7: Admin Command Center & Verification" feature for the RapidRelief project.

Here are the project architectural rules:
- The project uses a per-feature DbContext pattern (Entity Framework Core).
- Backend: ASP.NET Core API using Minimal Endpoints.
- Frontend: Blazor WebAssembly PWA.
- All modules must be isolated under `Features/CommandCenter`.
- Endpoints must handle a degraded database mode (D-005) by checking `DatabaseHealth.PostgresAvailable`.
- The UI should be protected with the `[Authorize]` attribute and require Admin roles.

For F7, please:
1. Build a Dashboard page in the Blazor client that gives admins an overview of active incidents, shelters, and system health.
2. Build the backend endpoints (`CommandCenterEndpoints`) to supply this data.
3. As noted in PROJECT-CONTEXT.md, build this against the "fake read services" first (e.g., `FakeIncidentReadService`, `FakeShelterReadService`) available in the Contracts layer before wiring up a real database if needed.
4. Ensure the UI looks premium, dynamic, and follows modern web aesthetics (no plain tables, use cards and status badges).
```

---

## F12: Analytics, Heatmaps & Response Metrics

**Prompt:**
```text
I need to implement the "F12: Analytics, Heatmaps & Response Metrics" feature for the RapidRelief project.

Here are the project architectural rules:
- The project uses a per-feature DbContext pattern (Entity Framework Core).
- Backend: ASP.NET Core API using Minimal Endpoints.
- Frontend: Blazor WebAssembly PWA.
- All modules must be isolated under `Features/Analytics`.
- Endpoints must handle a degraded database mode (D-005) by checking `DatabaseHealth.PostgresAvailable`.

For F12, please:
1. Note from PROJECT-CONTEXT.md: This feature is "Read-only via contracts". This means it should aggregate data using the shared interfaces in `RapidRelief.Shared.Contracts` (e.g., `IIncidentReadService`) and not write to its own transactional tables.
2. Build `AnalyticsEndpoints` to serve aggregated metrics (total incidents by severity, response times, etc.).
3. Build an `AnalyticsDashboard.razor` page in the client.
4. Integrate a Heatmap feature using the existing vendored Leaflet library (`wwwroot/lib/leaflet` and `rapidMap.js`).
5. Design the UI to look premium with charts/stats cards.
```

---

## F14: Audit Trail

**Prompt:**
```text
I need to implement the "F14: Audit Trail" feature for the RapidRelief project.

Here are the project architectural rules:
- The project uses a per-feature DbContext pattern (Entity Framework Core).
- Backend: ASP.NET Core API using Minimal Endpoints.
- Frontend: Blazor WebAssembly PWA.
- All modules must be isolated under `Features/AuditTrail`.
- Endpoints must handle a degraded database mode (D-005) by checking `DatabaseHealth.PostgresAvailable`.

For F14, please:
1. Note from PROJECT-CONTEXT.md: This feature is a "Pure event subscriber".
2. Create an `AuditDbContext` to store audit logs.
3. Subscribe to the existing `IEventBus` (which handles events like `PingCreated`, `IncidentReported`, etc.) and log these events to the `AuditDbContext`.
4. Create an endpoint `AuditEndpoints` to fetch a paginated list of audit logs (Admin only).
5. Create a Blazor page `AuditLog.razor` to display a professional, filterable, and searchable data table of all system events.
```

---

## F17: Safety Zones & Road Closures (Stretch Goal)

**Prompt:**
```text
I need to implement the "F17: Safety Zones & Road Closures" feature for the RapidRelief project.

Here are the project architectural rules:
- The project uses a per-feature DbContext pattern (Entity Framework Core).
- Backend: ASP.NET Core API using Minimal Endpoints.
- Frontend: Blazor WebAssembly PWA.
- All modules must be isolated under `Features/SafetyZones`.
- Endpoints must handle a degraded database mode (D-005) by checking `DatabaseHealth.PostgresAvailable`.

For F17, please:
1. Create a `SafetyZonesDbContext` with entities for `SafetyZone` (polygons/radius) and `RoadClosure` (lines/points).
2. Create `SafetyZoneEndpoints` for Admins to CRUD these zones and closures.
3. Integrate these zones into the Leaflet map on the Blazor frontend so users can visually see safe areas and blocked roads.
4. Implement degraded mode correctly so the map just gracefully skips loading zones if the database is down.
```
