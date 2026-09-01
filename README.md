# RapidRelief

**AI Smart Disaster Response & Emergency Management System** — semester project by Tayeb, Shehab, Tanjim & Mugdho.

Citizens report disasters (GPS, photos, offline-capable SOS) → AI classifies, scores priority, and detects duplicates → rescue teams run missions from a live priority queue → a government command center monitors, verifies, and dispatches → relief resources are requested, allocated, and tracked to delivery.

**Stack:** ASP.NET Core 8 (modular monolith) · Blazor WebAssembly PWA · PostgreSQL + EF Core · SignalR · Leaflet/OpenStreetMap · Gemini (with rule-based fallback).

## Start here

| Doc                                                                | Purpose                                                                                                                             |
| ------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md)                           | **Single source of truth** — what's implemented, what's next, architecture rules. Humans and AI agents read this before any change. |
| [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md) | Full development plan — features, ownership, phases, zero-blocking parallel model, demo script.                                     |
| [AGENTS.md](AGENTS.md)                                             | Instructions for AI coding agents (Copilot, Antigravity, etc.).                                                                     |

> Status: planning complete — implementation starts with F0 (Week 1 foundation). See the status board in PROJECT-CONTEXT.md.

## Run guide

Prerequisite: **.NET 8 SDK** only. Everything below works from a fresh clone.

### 1. Primary path — Docker Postgres

```bash
cp .env.example .env          # optional; compose has the same defaults built in
docker compose up -d          # postgres:16 on localhost:5432
dotnet run --project src/RapidRelief.Api
```

Open http://localhost:5179 — migrations apply automatically at startup (per-module, see D-005/D-007). Try the `/sample` page: it lists pings and posts one as `Admin` via the `X-Dev-Role` dev header.

### 2. Fallback — cloud Postgres (Neon/Supabase free tier)

No Docker? Point the app at any Postgres with an env var (or `dotnet user-secrets`):

```powershell
$env:ConnectionStrings__Postgres = "Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require"
dotnet run --project src/RapidRelief.Api
```

> **Ops note:** never run the `Development` environment against a production database — the startup seeder would create the demo users (`citizen1@rr.dev` … `Demo!123`) in it. Deployed instances must run with `ASPNETCORE_ENVIRONMENT=Production` (roles are seeded everywhere; demo users only in Development/Testing).

### 3. No database at all — degraded mode (D-005)

`dotnet run --project src/RapidRelief.Api` with nothing listening on 5432 still works: startup retries migrations 3× (2s backoff), logs a prominent warning, and keeps serving. Stub-backed pages keep working; DB-backed endpoints (e.g. `POST`/`GET /api/sample/pings`) return **503 ProblemDetails**; `GET /health` reports `"status": "degraded", "dbConnected": false`. The demo never depends on a network.

### Tests — no Docker/Postgres needed

```bash
dotnet test
```

Integration tests boot the real app under env `Testing` with per-context SQLite `:memory:` databases (see `tests/RapidRelief.Api.Tests/TestingWebAppFactory.cs`). Postgres SQL fidelity is proven separately by the CI `postgres-fidelity` job, which applies all migrations against a real `postgres:16` service.

### EF migrations (per-context, always)

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/RapidRelief.Api --context SampleDbContext --output-dir Features/Sample/Data/Migrations
dotnet ef migrations list --project src/RapidRelief.Api --context SampleDbContext
```

Every future context (`AuthDbContext`, `IncidentsDbContext`, …) copies this pattern: **always** pass `--context` and its own feature-owned `--output-dir` (PROJECT-CONTEXT §4.4).
