# RapidRelief

**AI Smart Disaster Response & Emergency Management System** — semester project by Tayeb, Shehab, Tanjim & Mugdho.

Citizens report disasters (GPS, photos, offline-capable SOS) → AI classifies, scores priority, and detects duplicates → rescue teams run missions from a live priority queue → a government command center monitors, verifies, and dispatches → relief resources are requested, allocated, and tracked to delivery.

**Stack:** ASP.NET Core 8 (modular monolith) · Blazor WebAssembly PWA · PostgreSQL + EF Core · SignalR · Leaflet/OpenStreetMap · Gemini (with rule-based fallback).

## Start here

| Doc | Purpose |
|---|---|
| [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) | **Single source of truth** — what's implemented, what's next, architecture rules. Humans and AI agents read this before any change. |
| [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md) | Full development plan — features, ownership, phases, zero-blocking parallel model, demo script. |
| [AGENTS.md](AGENTS.md) | Instructions for AI coding agents (Copilot, Antigravity, etc.). |

> Status: planning complete — implementation starts with F0 (Week 1 foundation). See the status board in PROJECT-CONTEXT.md.
