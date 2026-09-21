# RapidRelief — Technology Stack & Architectural Guide

> **Purpose of this document:** A comprehensive mentoring guide explaining the complete technology stack, frameworks, architectural patterns, and engineering decisions used across the RapidRelief project.

---

## 1. High-Level Overview

**RapidRelief** is an **AI-Smart Disaster Response & Emergency Management System** built with a single-language ecosystem (**C# / .NET 8**) for both server and browser clients.

### Core Architecture
- **Type:** ASP.NET Core Hosted Progressive Web Application (PWA)
- **Pattern:** Modular Monolith with Vertical Slice Architecture
- **Language:** C# 12 / .NET 8 across all layers

---

## 2. Technology Stack Breakdown

```text
┌───────────────────────────────────────────────────────────────┐
│              RapidRelief.Client (Frontend - SPA)              │
│       Blazor WebAssembly (WASM) .NET 8 · PWA · Leaflet.js     │
└───────────────────────────────┬───────────────────────────────┘
                                │ JSON / REST / WebSockets (SignalR)
┌───────────────────────────────▼───────────────────────────────┐
│               RapidRelief.Api (Backend - Server)              │
│  ASP.NET Core 8 · Minimal APIs · FluentValidation · Serilog   │
│           In-Process Event Bus · MultiAuth (JWT + Dev)        │
└──────────────┬───────────────────────────────┬────────────────┘
               │                               │
┌──────────────▼────────────────┐ ┌────────────▼────────────────┐
│  PostgreSQL (Database Layer)  │ │      AI Analysis Pipeline   │
│   EF Core 8 (Npgsql)          │ │  OpenRouter (DeepSeek/Meta) │
│   Per-Slice DbContexts        │ │  + Rule-Based Fallback      │
└───────────────────────────────┘ └─────────────────────────────┘
```

---

### 🔹 Frontend (Client Layer)

| Technology | Role & Usage | Why It Was Chosen |
| :--- | :--- | :--- |
| **Blazor WebAssembly (WASM) .NET 8** | Single Page Application (SPA) running client-side in the browser via WebAssembly. | Allows full-stack C# development, sharing models/contracts directly with the backend without JS duplication. |
| **Progressive Web App (PWA)** | Service worker caching, offline capability, installability on mobile/desktop. | Enables disaster reporting in field conditions with low or no internet connectivity. |
| **Vanilla CSS & HTML5** | Modern, responsive layout with a token design system (`wwwroot/css/app.css`, light + dark via `data-theme`; Forest Green action / Rescue Red emergency palette — see [design.md](design.md)). | Maximum styling control, zero heavy JS UI framework overhead. |
| **Leaflet.js + OpenStreetMap** | Interactive mapping, shelter locator, incident geo-tagging, and heatmaps. | 100% open-source, free tile server, no Google Maps API keys or billing required. |
| **Microsoft.AspNetCore.SignalR.Client** | Real-time push updates for live alert feeds, chat, and status changes. | Instant bidirectional messaging over WebSockets with automatic fallback. |

---

### 🔹 Backend (API Layer)

| Technology | Role & Usage | Why It Was Chosen |
| :--- | :--- | :--- |
| **ASP.NET Core 8 Web API** | Modular monolith host serving Minimal API endpoints and the Blazor client. | High throughput, unified hosting, built-in dependency injection, and native AOT readiness. |
| **Minimal APIs** | Lightweight endpoint route definition per vertical slice. | Fast startup, clean routing syntax, minimal boilerplate compared to heavy MVC controllers. |
| **FluentValidation** | Explicit request validation before hitting domain handlers. | Strong typed rules, testable, and produces standard RFC 7807 `ProblemDetails` errors. |
| **Serilog (`Serilog.AspNetCore`)** | Structured JSON and console logging across application lifecycle. | Structured diagnostic events, easy troubleshooting, and configurable log sinks. |
| **MultiAuth (JWT + FakeAuth)** | Multi-scheme authentication handling real JWT tokens (password login and Google/Neon sign-in) plus the `X-Dev-Role` header in Development. | Zero friction for frontend developers testing the three roles (Citizen, Rescuer, Government) without logging in/out. |
| **In-Process Scoped Event Bus** | Lightweight domain event dispatcher (`IEventBus`) decoupling slices. | Slices can notify other modules (e.g. `IncidentCreated`, `AlertPublished`) without referencing their code directly. |

---

### 🔹 Database & Persistence

| Technology | Role & Usage | Why It Was Chosen |
| :--- | :--- | :--- |
| **PostgreSQL 16** | Primary relational database engine. | Robust ACID compliance, native JSON/Geo support, free cloud tiers (Neon DB) and local Docker support. |
| **Entity Framework Core 8 (Npgsql)** | Object-Relational Mapper (ORM). | Strongly typed LINQ queries, automatic migrations, and connection pooling. |
| **Per-Slice DbContext Pattern** | Each feature owns its own `DbContext` — nine today (`SampleDbContext`, `AuthDbContext`, `AiDbContext`, `NotificationsDbContext`, `OpsDbContext`, `AlertsDbContext`, `IncidentsDbContext`, `ReliefDbContext`, `RescueDbContext`) with independent migration history tables (`__efmigrationshistory_*`). | Prevents team migration merge conflicts across multiple developers. |
| **Database Degraded Mode** | `MigrationRunner` retries on startup and falls back to degraded mode if DB is unreachable. | The application never crashes on DB failure; read-only/stub features remain functional. |

---

### 🔹 AI Engine

| Technology | Role & Usage | Why It Was Chosen |
| :--- | :--- | :--- |
| **OpenRouter API** | Cloud LLM gateway pinned to free-tier models: text `z-ai/glm-5.2:free` → `nvidia/nemotron-3-super-120b-a12b:free`, vision `google/gemma-4-31b-it:free` → `minimax/minimax-m3:free` (D-061). Opt-in via `Ai:OpenRouter:ApiKey`. | One API for many providers, in-body model fallback, and zero vendor lock-in. |
| **Rule-Based Fallback Engine** | Permanent offline/fallback AI classifier and chatbot engine. | Guarantees the system operates 100% reliably even if the internet drops, API keys expire, or rate limits are reached. |

---

### 🔹 Testing & Quality Assurance

| Technology | Role & Usage | Why It Was Chosen |
| :--- | :--- | :--- |
| **xUnit** | Primary unit and integration test runner. | Industry standard for .NET automated testing. |
| **Microsoft.AspNetCore.Mvc.Testing** | In-memory web server testing with `TestingWebAppFactory`. | Boots full API in memory for fast end-to-end HTTP request testing. |
| **SQLite In-Memory Provider** | Replaces PostgreSQL during integration tests. | Fast test execution without requiring a live database connection. |
| **NetArchTest.eNhancedEdition** | Architecture rule enforcement tests. | Automatically verifies slice isolation (e.g., preventing Feature A from referencing Feature B). |

---

## 3. Solution Projects Structure

```text
RapidRelief/
├── src/
│   ├── RapidRelief.Api/          # Backend API & Host for Blazor Client
│   │   ├── Features/             # Vertical slices (Endpoints, DbContext, Handlers)
│   │   └── Infrastructure/       # Auth, Eventing, Persistence, Module Discovery
│   │
│   ├── RapidRelief.Client/       # Frontend Blazor WASM Single Page Application
│   │   ├── Features/             # Feature UI components & Razor pages
│   │   ├── Layout/               # Main layout, navigation, role selector
│   │   └── wwwroot/              # Static assets, CSS, icons, service worker
│   │
│   └── RapidRelief.Shared/       # Shared Kernel
│       └── Contracts/            # DTOs, Enums, Interfaces, Domain Events
│
└── tests/
    ├── RapidRelief.Api.Tests/           # Integration & endpoint tests
    └── RapidRelief.Architecture.Tests/  # Architectural boundary guard tests
```

---

## 4. Key Architectural Rules

1. **Vertical Slices:** Every feature lives in its own `Features/{FeatureName}` directory. Never reference another feature's folder directly.
2. **Contracts as the Single Surface:** Cross-module communication happens strictly via `RapidRelief.Shared.Contracts` interfaces and domain events.
3. **No Cross-Module Foreign Keys:** Related entities across different domains are referenced by plain `Guid` IDs.
4. **Resilience First:** External services (Database, AI, Cloud Storage) always sit behind interfaces with permanent offline fallbacks.

---

## 5. Quick Reference Commands

### Run the Application
```powershell
dotnet run --project src/RapidRelief.Api --launch-profile http
```
*Access the Web App at:* **`http://localhost:5179`**

### Run Automated Tests
```powershell
dotnet test RapidRelief.sln
```

### Apply Database Migrations
```powershell
# always name the context and its feature-owned output dir — see docs/api-conventions.md
dotnet ef database update --project src/RapidRelief.Api --context SampleDbContext
```
*Startup applies every context automatically; this command is only for CI or manual checks.*
