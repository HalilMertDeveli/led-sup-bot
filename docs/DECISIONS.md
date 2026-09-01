# Architecture Decisions

## ADR-001 — Official WhatsApp Cloud API only
**Status:** Superseded for current product constraint by ADR-013  
**Decision:** Production messaging uses WhatsApp Business Platform / Cloud API. Personal WhatsApp automation is rejected for compliant business use.

## ADR-002 — Hybrid workflow + AI extraction
**Status:** Accepted (Phase 1)  
**Decision:** Backend owns conversation state, slots, validation, handoff. AI extracts and phrases; it does not own the state machine or send permission.

## ADR-003 — .NET 8 LTS for backend
**Status:** Accepted (Phase 2)  
**Decision:** Target `net8.0` and pin SDK via `global.json` to 8.0.x for LTS stability. SDK 10 may be present on machines but is not the project target.

## ADR-004 — Clean architecture project split
**Status:** Accepted (Phase 2)  
**Decision:** `Domain` / `Application` / `Infrastructure` / `Api` / `Workers` separation with vendor ports (`IWhatsAppClient`, `IAiService`, `IMediaStorage`).

## ADR-005 — React + TypeScript admin (Vite)
**Status:** Accepted (Phase 2)  
**Decision:** Admin foundation uses Vite + React + TypeScript. Full dashboard deferred to Phase 9.

## ADR-006 — No vendor implementations in Phase 2
**Status:** Accepted (Phase 2)  
**Decision:** Do not register mock WhatsApp/OpenAI implementations as production services. Interfaces and options only until Phases 4 and 6.

## ADR-007 — In-memory work queue as foundation only
**Status:** Accepted (Phase 2)  
**Decision:** `InMemoryBackgroundWorkQueue` proves DI/queue boundary for local wiring. Production queue technology chosen later; must not be described as WhatsApp processing.

## ADR-008 — Secrets via env / future Key Vault
**Status:** Accepted (Phase 2)  
**Decision:** Empty placeholders in appsettings; `.env.example` placeholders only; flat env names bridged into options. Azure Key Vault (or equivalent) intended later — not required for Phase 2.

## ADR-009 — Deferred entity persistence to Phase 3
**Status:** Superseded by ADR-010  

## ADR-010 — PostgreSQL + EF Core code-first
**Status:** Accepted (Phase 3)  
**Decision:** Persist domain model with Npgsql EF Core 8, Fluent API configurations, and checked-in migrations. Do not auto-migrate on production startup. Use Testcontainers PostgreSQL for integration tests (not EF InMemory).

## ADR-011 — Customer delete restrict
**Status:** Accepted (Phase 3)  
**Decision:** `Customer` → `Conversation` uses `Restrict` so customer rows cannot cascade-wipe support history.

## ADR-012 — BotMode and ConversationStage naming
**Status:** Accepted (Phase 3)  
**Decision:** Persist `BotMode` as `Bot`/`Paused`/`Human` and stages as `ConversationStage` enum (string storage). Behavior of Human silence belongs to Phase 8.

## ADR-013 — Personal WhatsApp connector (risk-accepted)
**Status:** Accepted pending Halil risk acknowledgment (Phase 4 audit)  
**Decision:** Support number must remain a consumer personal WhatsApp account usable in the normal mobile app. Official Cloud API / Business App Coexistence does not meet that constraint. Recommended path: ASP.NET Core ↔ Node linked-device connector (Baileys-class or Evolution API) behind `IWhatsAppClient`, with explicit ToS/ban risk. Smoke-test in Phase 4.5 before production. Revisit Cloud API if requirements change (Business number or dedicated API number).

## ADR-014 — Baileys thin sidecar for Phase 4.5
**Status:** Accepted (Phase 4.5)  
**Decision:** Implement `connector/` with `@whiskeysockets/baileys@6.7.24` + Express on loopback, webhook to ASP.NET with shared secret. Prefer over Evolution API for minimal ops in 4.5. No automatic customer replies. Not Meta-approved.

## Open decisions (still required from Halil)
1. Explicit acceptance of unofficial WhatsApp ToS/ban risk on the personal work number (Yes/No)  
2. Complete Phase 4.5 real QR + controlled message verification  
3. Prefer a second dedicated number (safer) vs automate the personal number  
4. Hosting (always-on PC vs VPS/cloud) and PostgreSQL host  
5. Auth provider for technicians  
6. Label photo completion rule  
7. Data retention policy  
8. Optional future path: WhatsApp Business App + official Coexistence  
