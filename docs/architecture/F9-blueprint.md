Read the research report and the repo. Everything checks out (paths cited below all exist). One correction to the brief's Q3 recommendation is flagged in D-032 — a boolean switch cannot satisfy the DoD and §4.5 simultaneously.

---

# F9 — Real-Time Hub & Notification Center — Implementation Blueprint

Owner: Tayeb · Lane: `Features/Realtime` (Api) + `Common/Realtime` (Client) · Depends on: F0, F1, F8 (all DONE).

## DECISIONS

| ID | Decision | Why |
|---|---|---|
| **D-032** | **`Realtime:Mode` tri-state, not a boolean:** `Hub` (default) = `AddSignalR` + `MapHub` + persist&push · `PollingOnly` = no SignalR registration, no hub route, notifier still **persists** (endpoints serve polling) · `Off` = `NoOpRealtimeNotifier`, no hub. Endpoints are mapped in all three modes. | A boolean `Enabled=false ⇒ NoOp` writes no rows, so polling would show *nothing* — that is not "degrades to polling" (DoD). `PollingOnly` proves the DoD; `Off` keeps the no-op alive and test-pinned per §4.5. One config key, three lines of `switch`. |
| **D-033** | **Payload cap = drop-and-log** at 4 000 chars of serialized JSON (matches F8's description cap). No row written, no push; log `Topic`, `Audience`, byte length only — never the payload. | Truncated JSON is unparseable garbage in the inbox; a dropped notification is a caller bug that must be loud in logs and invisible to users. Metadata-only logging preserves the F8 no-PII-in-logs carry-out. |
| **D-034** | **30-day retention sweep.** `NotificationRetentionWorker : BackgroundService`, `PeriodicTimer` every `Realtime:RetentionSweepHours` (6), deletes `notifications_notification` older than `Realtime:RetentionDays` (30) in batches of 500; read rows cascade. Skipped while degraded. | Unbounded growth on a shared demo DB is a slow leak with no owner; the sweeper is ~40 lines against the existing `AiAnalysisWorker` pattern and bounds the cursor/COUNT queries too. |
| **D-035** | **Dev transport:** when the client has no JWT session and `DevRoleState.CurrentRole` is set, the hub connection is built with `HttpTransportType.LongPolling` + `o.Headers["X-Dev-Role"]`. Real sessions use default transports (WS first). **No `AuthSetup.cs` change.** | Browser WebSockets cannot carry custom headers (research R2) — `X-Dev-Role` survives negotiate/long-polling only. A `?devRole=` query hook would widen the auth surface for dev-only gain. |
| **D-036** | **Topic convention** `feature.entity.action`, lowercase, `[a-z0-9.]`, ≤64 chars; notifier sanitizes + logs on violation. **F9 subscribes to exactly two contract events**: `IncidentAssessed` → `ai.incident.assessed` to `role:Rescue` + `role:Admin`; `AlertPublished` → `alerts.published` to All (handler ships now, dormant until F10 publishes). `AuthEvent` produces **no** notification — it only drives disconnects. | Keeps slice isolation (events only), gives F5/F7/F10 a live feed on day one, and keeps security-relevant auth activity out of a user-visible inbox. |
| **D-037** | **`Summary` is derived, not contracted:** after serializing `payload`, if the JSON root has a string `title` or `summary` property, take it (control-stripped, ≤160 chars); else `Summary = Topic`. Stored on the row. | `IRealtimeNotifier` is FROZEN (`object payload`) — the UI still needs one renderable line. Duck-typing over JSON needs no contract change and degrades to the topic. |
| **D-038** | **Cursor + paging:** single `since` param, opaque `base64url("{CreatedAtUtc.UtcTicks}:{Id:D}")`. Present ⇒ keyset `(CreatedAtUtc, Id) > cursor` ascending. Absent ⇒ newest `limit` rows (desc) reversed to ascending. `limit` clamped 1–100 (default 50). Undecodable cursor ⇒ 400. **No deep history paging in v1** — retention window (30 d) is the archive. | One parameter serves both inbox bootstrap and incremental polling; composite keyset survives same-tick rows; opaque form stops clients inventing clock values. |
| **D-039** | **Unread count** = plain `COUNT` over the audience predicate `NOT EXISTS(read)`, bounded by the retention window; UI displays `99+` above 99. No denormalized counter. | Demo volumes are trivial and the `(Audience, Role, CreatedAtUtc)` index covers it; a counter column adds an invalidation story with no payoff. |
| **D-040** | **Registry bounds:** `HubConnectionRegistry` caps 10 tracked connections/user and 2 000 tracked users; over-cap connections are simply **not tracked** (warn-logged, throttled) — they still die at token expiry. `OnDisconnectedAsync` always removes; empty per-user buckets are pruned. | Keeps the bespoke registry (risk 2) from becoming an unbounded memory sink; the failure mode degrades to D-020's ≤31-min window rather than to OOM. |
| **D-041** | **Packages:** server none (SignalR is in `Microsoft.NET.Sdk.Web`'s shared framework); `Microsoft.AspNetCore.SignalR.Client` **8.0.30** added to `RapidRelief.Client` **and** `RapidRelief.Api.Tests`. **MessagePack rejected.** | Matches every existing 8.0.30 pin; MessagePack adds WASM payload + trimming friction for sub-KB JSON. |
| **D-042** | **Table prefix `notifications_`** (`notifications_notification`, `notifications_read`) although the folder is `Features/Realtime`. | §5 wants a table family prefix; `notifications_` names the data, `realtime_` names the transport. Recorded so nobody "fixes" it later. |

## BLUEPRINT

### File tree (new files only)

```
src/RapidRelief.Api/Features/Realtime/
  RealtimeModule.cs                       (edit: D-032 switch, SignalR, DbContext, handlers, MapHub, MigrateAsync)
  NoOpRealtimeNotifier.cs                 (unchanged — Mode=Off slot)
  SignalRRealtimeNotifier.cs
  RealtimeOptions.cs                      (Mode enum + bound config values)
  Domain/Notification.cs · Domain/NotificationRead.cs
  Data/NotificationsDbContext.cs · Data/Migrations/*_Initial.cs
  Hub/NotificationsHub.cs · Hub/HubConnectionRegistry.cs
  Handlers/IncidentAssessedNotificationHandler.cs
  Handlers/AlertPublishedNotificationHandler.cs
  Handlers/AuthEventDisconnectHandler.cs
  Endpoints/NotificationEndpoints.cs      (+ feature-local wire records)
  Pipeline/NotificationRetentionWorker.cs
  Pipeline/NotificationCursor.cs

src/RapidRelief.Client/Common/Realtime/
  NotificationModels.cs · NotificationsApi.cs · NotificationHubClient.cs · NotificationState.cs
  NotificationBell.razor · NotificationInbox.razor · ToastHost.razor · ToastHost.razor.css

tests/RapidRelief.Api.Tests/Realtime/
  HubConnectionTests.cs · HubAuthTests.cs · NotifierTests.cs
  NotificationEndpointTests.cs · RealtimeModeTests.cs · RetentionTests.cs · RegistryTests.cs
tests/RapidRelief.Architecture.Tests/SerilogQueryLeakTests.cs
```

Edits: [src/RapidRelief.Api/Program.cs](src/RapidRelief.Api/Program.cs) (one `realtime` rate-limit policy), [src/RapidRelief.Api/appsettings.json](src/RapidRelief.Api/appsettings.json), [tests/RapidRelief.Api.Tests/TestingWebAppFactory.cs](tests/RapidRelief.Api.Tests/TestingWebAppFactory.cs) (+2 lines), [src/RapidRelief.Client/Program.cs](src/RapidRelief.Client/Program.cs), [src/RapidRelief.Client/Layout/MainLayout.razor](src/RapidRelief.Client/Layout/MainLayout.razor), [src/RapidRelief.Client/Layout/NavMenu.razor](src/RapidRelief.Client/Layout/NavMenu.razor), both `.csproj` per D-041, [docs/architecture/](docs/architecture) (F9 blueprint), README, PROJECT-CONTEXT.

### Entities + DbContext

`Notification`: `Id Guid PK` · `Audience string(8)` (`All|Role|User`) · `Role string?(16)` · `UserId Guid?` (**bare Guid, no FK — §4.3**) · `Topic string(64) req` · `Summary string(160) req` · `PayloadJson string(4000) req` · `CreatedAtUtc DateTimeOffset`.
`NotificationRead`: composite PK `(NotificationId, UserId)` · `ReadAtUtc DateTimeOffset`. FK `NotificationId → notifications_notification` with `Cascade` (same context, allowed).

`NotificationsDbContext` copies [AiDbContext](src/RapidRelief.Api/Features/Ai/Data/AiDbContext.cs) exactly: `MigrationsHistoryTableName = "__efmigrationshistory_notifications"`, `ToTable(...)`, and the **SQLite ticks-gate converter on `CreatedAtUtc` and `ReadAtUtc`** (both appear in `WHERE`/`ORDER BY`). Indexes: `(CreatedAtUtc, Id)` (keyset), `(Audience, Role, CreatedAtUtc)` (fan-out filter), `(UserId, CreatedAtUtc)` (targeted), reads `(UserId, NotificationId)`.

```
dotnet ef migrations add Initial --context NotificationsDbContext \
  --project src/RapidRelief.Api --output-dir Features/Realtime/Data/Migrations
```
Never touch Sample/Auth/Ai migrations.

### `SignalRRealtimeNotifier`

Singleton implementing the frozen `IRealtimeNotifier`; ctor takes `IServiceScopeFactory`, `IHubContext<NotificationsHub>?` (resolved via `GetService`, null in `PollingOnly`), `TimeProvider`, `ILogger`. All three methods funnel into `PublishAsync(audience, role, userId, topic, payload, ct)`:

1. `JsonSerializer.Serialize(payload)` → if `> 4000` chars: log metadata-only, **return** (D-033).
2. Sanitize topic (D-036); derive `Summary` (D-037).
3. New scope → `NotificationsDbContext`; if `DatabaseHealth.PostgresAvailable != true`, **skip persist and still push** (mirrors D-028 degraded behaviour).
4. Push: `All` → `Clients.All`; `Role` → `Clients.Group($"role:{role}")`; `User` → `Clients.User(userId.ToString("D"))` (default `IUserIdProvider` reads `ClaimTypes.NameIdentifier`, emitted by both `TokenService` and [FakeAuthHandler](src/RapidRelief.Api/Infrastructure/Auth/FakeAuthHandler.cs)). Method name `"notification"`, single `NotificationDto` argument.
5. **Whole body wrapped in try/catch** (except `OperationCanceledException` on `ct`) → log error, return. The bus runs handlers inline in the publisher's request (D-006), so a hub or DB fault must never surface in F2/F8/F10.

Callers today: **none** — F9 is both producer and consumer. Producers are F9's own handlers (D-036); F5/F7/F10/F11 wire in later with zero F9 changes.

### `NotificationsHub`

`[Authorize]`, `sealed`, **zero public methods** (push-only; `StreamBufferCapacity`/`MaximumParallelInvocationsPerClient` therefore irrelevant).
- `OnConnectedAsync`: for each `Context.User.FindAll(ClaimTypes.Role)` → `Groups.AddToGroupAsync(ConnectionId, $"role:{r.Value}")` — **server-derived only, never a client argument**; then `registry.Add(userId, Context)`.
- `OnDisconnectedAsync`: `registry.Remove(userId, ConnectionId)`; always runs, including on `Abort()`.
- Module registration: `services.AddSignalR().AddHubOptions<NotificationsHub>(o => { o.MaximumReceiveMessageSize = 2*1024; o.EnableDetailedErrors = false; })`.
- Mapping: `endpoints.MapHub<NotificationsHub>("/hubs/notifications", o => { o.CloseOnAuthenticationExpiration = true; o.Transports = WebSockets | LongPolling; })`. Path **must** start `/hubs` or the `access_token` hook in [AuthSetup.cs](src/RapidRelief.Api/Infrastructure/Auth/AuthSetup.cs) silently no-ops. `RealtimeModule` caches the mode in a private field during `AddModule` (Program.cs reuses the same module instances for `MapEndpoints`).

### `HubConnectionRegistry` + `AuthEventDisconnectHandler`

Singleton `ConcurrentDictionary<Guid, ConcurrentDictionary<string, HubCallerContext>>` with D-040 caps. `AbortUser(Guid)` snapshots the bucket, calls `ctx.Abort()` on each (per-connection try/catch), returns the count. `AuthEventDisconnectHandler : IEventHandler<AuthEvent>` (scoped) aborts on `Action ∈ {"Lock","RoleChange","TokenReuse"}` — all published today by [IdentityUserAdminService](src/RapidRelief.Api/Features/Auth/Services/IdentityUserAdminService.cs) and `TokenService`. Logs `UserId` + aborted count only. Leak safety is pinned by a test asserting the dictionary is empty after disconnect.

### Endpoints — `/api/realtime/notifications`

Group: `.RequireAuthorization()` (any authenticated role) `.RequireRateLimiting("realtime")` + the `CacheControlNoStoreFilter` pattern from [AiEndpoints](src/RapidRelief.Api/Features/Ai/Endpoints/AiEndpoints.cs). All DB-backed ⇒ 503 `DatabaseUnavailable()` when degraded (D-005).

Audience predicate for caller `(userId, roles)`: `Audience == "All" || (Audience == "Role" && roles.Contains(Role)) || (Audience == "User" && UserId == userId)`. Read flag = `NOT EXISTS(read row for (id, userId))`.

- `GET ?since=&limit=` → `ApiEnvelope<NotificationPage>` where `NotificationPage(IReadOnlyList<NotificationDto> Items, DateTimeOffset ServerTimeUtc, string? NextCursor)`; paging per D-038; `NextCursor` = cursor of the newest returned item, else echo `since`.
- `PATCH /{id:guid}/read` → visibility check first (404 if not visible to caller — **never** reveal other users' rows), insert read row, duplicate PK swallowed → 204.
- `POST /read-all` → inserts read rows for all visible unread rows, capped 1 000/call, returns `ApiEnvelope<MarkedResponse(int Marked)>`.
- `GET /unread-count` → `ApiEnvelope<UnreadCountResponse(int Count)>` per D-039.

Wire records are feature-local (D-019 precedent) — `Shared/Contracts` is untouched by F9.

### Client

- **`NotificationsApi`** (singleton) — owns its own `HttpClient` built with the same `DevRoleHandler → AuthMessageHandler → HttpClientHandler` chain as [Program.cs](src/RapidRelief.Client/Program.cs) (the main client is scoped and cannot be injected into a singleton).
- **`NotificationHubClient`** (singleton): builds the connection with `AccessTokenProvider = async () => { await authApi.TryRefreshAsync(); return jwtState.AccessToken; }` (fires before every HTTP request incl. reconnects — reuse `AuthApi`'s single-flight refresh, never the handler chain), `.WithAutomaticReconnect([0s, 2s, 10s, 30s])`, D-035 dev transport. Subscribes to `AuthenticationStateChanged`: start when `HasSession`, `StopAsync`+`DisposeAsync` on logout. `On<NotificationDto>("notification", …)` → `NotificationState.Upsert`. All start/stop paths are try/catch — a dead hub must never surface as a UI error.
- **`NotificationState`** (singleton): `Dictionary<Guid, NotificationDto>` fed by push **and** poll, deduped by `Id`; `UnreadCount`; `Changed` event; `PeriodicTimer` poll loop at `Realtime:PollSecondsDisconnected` (5) / `Realtime:PollSecondsConnected` (60) chosen from `HubConnection.State`; keeps the last `NextCursor`.
- **UI**: `NotificationBell` (badge + dropdown) and `ToastHost` (push-only arrivals, max 3, auto-dismiss 6 s) in `MainLayout` inside `<AuthorizeView><Authorized>`; `NotificationInbox` at `/notifications` with a NavMenu link. **Rendering: plain `@n.Summary` / `@n.PayloadJson` interpolation only — `MarkupString` and `innerHTML` are forbidden for model text** (binding carry-out).
- No browser storage APIs; the published service worker only caches GET navigations, so `/hubs/*` passes through — **add no cache rule for `/hubs` or `/api`**.

### Config

`appsettings.json`: `"Realtime": { "Mode": "Hub", "RetentionDays": 30, "RetentionSweepHours": 6, "PollSecondsConnected": 60, "PollSecondsDisconnected": 5 }` and `"RateLimiting": { "Realtime": { "PermitLimit": 120, "WindowSeconds": 60 } }`. `Program.cs` gains the matching named policy next to `"ai"`. `IncludeQueryInRequestPath` is **never** set (defaults false) — pinned by `SerilogQueryLeakTests`.

## IMPLEMENTATION CHUNKS

**Chunk 1 — server.** Entities · `NotificationsDbContext` + `Initial` migration · `RealtimeOptions`/D-032 switch in `RealtimeModule` · `SignalRRealtimeNotifier` · `NotificationsHub` + `HubConnectionRegistry` · `AuthEventDisconnectHandler` · the two notification handlers · `NotificationEndpoints` + `NotificationCursor` · `NotificationRetentionWorker` · `realtime` rate-limit policy + appsettings · `TestingWebAppFactory` (+2 lines, + SignalR.Client package) · all server tests incl. LongPolling hub integration and the raw negotiate query-token test.
Verify: `dotnet build RapidRelief.sln -warnaserror` · `dotnet test RapidRelief.sln` · `dotnet ef migrations list --context NotificationsDbContext` (no DB needed). Offline throughout.

**Chunk 2 — client + docs.** SignalR.Client package · `NotificationModels`/`NotificationsApi`/`NotificationHubClient`/`NotificationState` · bell/inbox/toasts · `Program.cs` DI + `MainLayout`/`NavMenu` wiring · `docs/architecture/F9-blueprint.md` + README realtime section · PROJECT-CONTEXT §2/§3/§7/§8 bookkeeping.
Verify: `dotnet build` · `dotnet publish src/RapidRelief.Api -c Release` (0 warnings) · `dotnet test` · manual two-browser check (real logins, not FakeAuth) for the < 2 s DoD and the `Mode=PollingOnly` degradation.

## TEST PLAN

Hub (LongPolling over `f.Server.CreateHandler()`, `X-Dev-Role` header): connects; receives a `role:Admin` broadcast; receives a user-targeted push; **group isolation** — a Citizen connection never sees an Admin-role message; connection survives reconnect and rejoins groups.
Auth: `POST /hubs/notifications/negotiate` unauthenticated → **401**; with `?access_token={real JWT}` → **200** (this is the only coverage of the `AuthSetup` query hook — the .NET LongPolling client uses the `Authorization` header).
Notifier: persists then pushes for all three audiences; **4 001-char payload → no row, no push, metadata-only log** (D-033); publisher never breaks when the hub throws *or* the DbContext throws; degraded DB → push happens, persist skipped.
Endpoints: cursor paging (incl. two rows sharing a tick), `limit` clamps, bad cursor → 400, audience filtering (Citizen cannot see a Rescue-role or another user's row), `PATCH /{id}/read` idempotent + 404 for invisible ids, `read-all` count, `unread-count` accuracy, 401 unauthenticated, `Cache-Control: no-store` on every response.
Lifecycle: `AuthEvent("Lock")` → connection aborted; registry empty after disconnect; over-cap connections are untracked, not leaked.
Modes: `Mode=PollingOnly` → negotiate **404**, notifier still persists, `GET` returns rows; `Mode=Off` → `IRealtimeNotifier` resolves to `NoOpRealtimeNotifier`, negotiate 404, endpoints still 200.
Retention: rows older than `RetentionDays` deleted, newer kept, read rows cascade.
Guards: `SerilogQueryLeakTests` — `new RequestLoggingOptions().IncludeQueryInRequestPath == false` **and** `Program.cs` source contains no `IncludeQueryInRequestPath` (`[CallerFilePath]`, D-031 precedent). DI smoke pin updated to the `SignalRRealtimeNotifier` displacement. **All 284 existing tests stay green.**

## DEFINITION OF DONE

Two browsers, **real logins**: an action in one appears in the other in < 2 s · `Realtime:Mode=PollingOnly` → no console errors, notifications still arrive within one poll cycle · `Mode=Off` → no-op notifier proven by test · unread badge, inbox and toasts work after refresh and after reconnect · locking a user drops their live connection immediately · full suite green, build/publish 0 warnings · PROJECT-CONTEXT §2/§3/§6-unchanged/§7 (D-032…D-042)/§8 updated in the same PR.

## RISKS

1. **Query-string token exposure** — mitigated by `/hubs`-only scoping, 30-min TTL (D-013), `CloseOnAuthenticationExpiration`, and the Serilog guard test; third-party proxy logs remain outside our control.
2. **Bespoke kick registry** (D-040) — bounded and disconnect-tested, but still the only piece of F9 holding framework object references; if it ever misbehaves, set `Mode=PollingOnly` and lockout reverts to the D-020 window.
3. **Inline bus + hub push** — every `Notify*` runs inside the publisher's request/worker scope; the blanket try/catch is load-bearing and must never be narrowed.
4. **Dev/WS divergence** (D-035) — FakeAuth exercises LongPolling only; the two-browser real-login DoD check is the *only* WebSocket coverage before demo. Do it in Week 8, not Week 10.
5. **Fan-out read state** — role/`All` rows plus a separate read table make unread-count and audience filters the easiest thing to get subtly wrong; the audience-filtering tests are non-negotiable.

**Open assumption for the team:** notification volume stays well under a few thousand rows/day. Above that, D-039's plain `COUNT` and D-038's no-deep-paging call both need revisiting.