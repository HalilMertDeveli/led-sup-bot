# Phase 3 — Database & Data Model

**Date:** 2026-08-31  
**Cycle:** AUDIT → IMPLEMENT → TEST → FIX → RETEST → VERIFY → REPORT  
**Status:** PASS (PostgreSQL migration + Testcontainers persistence tests executed)

## AUDIT

### Repository state
Phase 2 foundation present (Api/Application/Domain/Infrastructure/Workers/tests/admin). No prior EF Core schema.

### Existing persistence
None. Options/`DATABASE_CONNECTION_STRING` bridge existed; DbContext not registered.

### Conflicts
- Renamed Phase 2 `ConversationState` → `ConversationStage`, `BotMode.Active/HumanTakeover` → `Bot/Human`, expanded `SupportCaseStatus` and `MessageType` to match Phase 3 requirements.
- Renamed application DTO `AiExtractionResult` → `AiExtractionResponse` to avoid clash with persistence entity.

### Risks
- Docker Desktop required for Testcontainers integration tests.
- Connection string must be set for API DB health check / DbContext registration.

---

## Entity model

| Entity | Purpose |
|--------|---------|
| `Customer` | WhatsApp identity via unique `PhoneNumber` |
| `Conversation` | Thread with `Status`, `BotMode`, `CurrentStage` |
| `Message` | Inbound/outbound; `ExternalMessageId` for idempotency |
| `TechnicalSupportCase` | Support job (1:1 with conversation) |
| `ScreenInformation` | Width/Height + `DimensionUnit` (Cm/M) |
| `ReceiverInformation` | Count (+ optional manufacturer/model/notes) |
| `PanelLabelPhoto` | Multiple photos; `StorageKey` only (no binary) |
| `ConfigConnectionInfo` | Cable step status enum |
| `AnyDeskInformation` | Sensitive address + collected timestamp |
| `AiExtractionResult` | JSONB payload store for future AI (no OpenAI calls) |

## Relationships & delete behavior

```
Customer 1──* Conversation          (Restrict — preserve history)
Conversation 1──* Message           (Cascade)
Conversation 1──1 TechnicalSupportCase (Cascade)
Conversation 1──* AiExtractionResult   (Cascade)
Case 1──0..1 Screen / Receiver / Config / AnyDesk (Cascade)
Case 1──* PanelLabelPhoto              (Cascade)
Message 0..1──* AiExtractionResult     (SetNull on message FK)
```

**Decision:** Do not cascade-delete from `Customer` so accidental customer deletes cannot wipe history without an explicit conversation strategy later.

## Enums (persisted as strings)

- `ConversationStage`, `ConversationStatus`, `BotMode` (`Bot`/`Paused`/`Human`)
- `MessageDirection`, `MessageType` (incl. Audio/Video/Other), `MessageSender`, `DeliveryStatus`
- `SupportCaseStatus` (Open → Cancelled)
- `DimensionUnit`, `ConfigConnectionStatus`, `MediaAnalysisStatus`, `AiExtractionType`

## Indexes & constraints

| Index / constraint | Notes |
|--------------------|-------|
| `ix_customers_phone_number` | **UNIQUE** |
| `ix_conversations_customer_id` | |
| `ix_conversations_status` | |
| `ix_conversations_updated_at` | |
| `ix_conversations_status_updated_at` | Admin queue |
| `ix_messages_conversation_id` | |
| `ix_messages_created_at` | |
| `ix_messages_external_message_id` | **UNIQUE** filtered `WHERE ExternalMessageId IS NOT NULL` |
| `ix_technical_support_cases_conversation_id` | **UNIQUE** (1:1) |
| `ix_technical_support_cases_status` | |
| Unique case FKs for screen/receiver/config/anydesk | 1:1 owned rows |

## Migration strategy

- Code-first EF Core migrations in `src/LedSupBot.Infrastructure/Persistence/Migrations`
- **NOT** applied automatically on application startup (production-safe)
- Design-time factory: `ApplicationDbContextFactory`
- Production/deploy:

```bash
export DATABASE_CONNECTION_STRING="Host=...;Database=led_sup_bot;Username=...;Password=..."
dotnet ef database update --project src/LedSupBot.Infrastructure --startup-project src/LedSupBot.Api
```

Local:

```bash
docker compose up -d
$env:DATABASE_CONNECTION_STRING = "Host=localhost;Port=5432;Database=led_sup_bot;Username=postgres;Password=postgres"
dotnet ef database update --project src/LedSupBot.Infrastructure --startup-project src/LedSupBot.Api
```

## Test strategy

PostgreSQL via **Testcontainers** (`postgres:16-alpine`). Tests call `Database.MigrateAsync()` then exercise CRUD, uniqueness, and relationships. EF InMemory is **not** used as a PostgreSQL substitute.

## Privacy

- `AnyDeskAddress` treated as sensitive — no public API exposure in this phase; avoid logging.
- Phone numbers unique identifiers — minimize log inclusion.
- Photos store `StorageKey` only; binaries deferred to Phase 7.
- AI payloads in JSONB — no fake AI results created.

## Deferred

WhatsApp, OpenAI, workflow transitions, human-takeover behavior, media upload/download, admin dashboard, encryption-at-rest for AnyDesk (future hardening).

## Verification executed

- `dotnet ef migrations add InitialCreate`
- `dotnet ef database update` against `docker compose` Postgres 16
- `\dt` confirmed 10 domain tables + `__EFMigrationsHistory`
- Unique filtered index confirmed on `ExternalMessageId`
- `dotnet test`: 3 unit + 11 integration (incl. Postgres) passed
