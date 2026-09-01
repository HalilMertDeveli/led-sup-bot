# Phase 4.5 — Personal WhatsApp Connector Setup

**Date:** 2026-08-31  
**Connector:** Thin Node.js sidecar using `@whiskeysockets/baileys@6.7.24`  
**Official status:** **NOT Meta-approved.** Unofficial linked-device automation. ToS / ban / protocol-break risks apply.

---

## Why Baileys (not Evolution API)

| Factor | Baileys sidecar | Evolution API |
|--------|-----------------|---------------|
| Extra deps | Minimal (Express + Baileys) | Postgres/Redis/platform stack |
| ASP.NET fit | Localhost HTTP + webhook | REST/webhooks (heavier) |
| Event control | Direct `fromMe` / message map | Wrapped |
| Phase 4.5 goal | Prove personal link + ingest | Overkill |

**Selected:** Baileys sidecar under `connector/`.

---

## Architecture

```
Personal WhatsApp (phone)
  ↕ linked companion
Node connector (Baileys) :3100 loopback
  ↕ X-Connector-Secret
ASP.NET /internal/whatsapp/* 
  → IWhatsAppInboundEventHandler → PostgreSQL
  → IWhatsAppClient (ConnectorWhatsAppClient) → POST /send/text
```

No AI. No workflow automation. No automatic customer replies.

---

## Installation

```bash
# Postgres
docker compose up -d
$env:DATABASE_CONNECTION_STRING = "Host=localhost;Port=5432;Database=led_sup_bot;Username=postgres;Password=postgres"
dotnet ef database update --project src/LedSupBot.Infrastructure --startup-project src/LedSupBot.Api

# Shared secret (generate your own; never commit; never log)
$secret = [guid]::NewGuid().ToString("N")
$env:WHATSAPP_CONNECTOR_SHARED_SECRET = $secret
$env:CONNECTOR_SHARED_SECRET = $secret   # same value — connector .env alias
$env:WHATSAPP_CONNECTOR_ENABLED = "true"
$env:WHATSAPP_CONNECTOR_BASE_URL = "http://127.0.0.1:3100"

# API (must see WHATSAPP_CONNECTOR_SHARED_SECRET or CONNECTOR_SHARED_SECRET in process env)
dotnet run --project src/LedSupBot.Api --launch-profile http

# Connector (separate terminal)
cd connector
copy .env.example .env
# Put the SAME $secret into CONNECTOR_SHARED_SECRET (and optionally WHATSAPP_CONNECTOR_SHARED_SECRET)
# ASPNET_WEBHOOK_URL=http://127.0.0.1:5080/internal/whatsapp/events
npm install
npm start
```


Scan QR in the connector terminal: WhatsApp → Linked devices → Link a device.

---

## Session storage

- Path: `connector/.sessions/` (gitignored)
- Never commit, never log, never return via API
- Backup: stop connector, encrypt-copy `.sessions/` offline, restore before start
- Logout: delete `.sessions/` → new QR required

---

## HTTP contracts

### Inbound webhook (connector → ASP.NET)

`POST /internal/whatsapp/events`  
Header: `X-Connector-Secret`

```json
{
  "eventType": "IncomingMessage",
  "externalMessageId": "...",
  "remotePhoneNumber": "9055...",
  "fromMe": false,
  "botOriginated": false,
  "messageType": "Text",
  "textContent": "...",
  "timestamp": "2026-08-31T00:00:00Z",
  "media": { "mimeType": "image/jpeg", "downloadable": true }
}
```

Connection:

```json
{ "eventType": "ConnectionStateChanged", "status": "CONNECTED" }
```

Statuses: `DISCONNECTED | CONNECTING | CONNECTED | AUTHENTICATION_REQUIRED | ERROR`

### Outbound (ASP.NET → connector)

`POST http://127.0.0.1:3100/send/text`  
`POST /internal/whatsapp/send-text` (API proxy for controlled tests)

```json
{ "to": "9055...", "text": "controlled test only" }
```

---

## fromMe / human takeover preparation

| Signal | Mapping |
|--------|---------|
| `fromMe=false` | Customer inbound |
| `fromMe=true` + `botOriginated=true` | Bot outbound |
| `fromMe=true` + `botOriginated=false` | Technician (Halil) → `BotMode.Human` |

**Bot correlation (deterministic):**

1. Connector marks `botSentIds` in memory on `/send/text` and sets `botOriginated` on the echo.
2. ASP.NET immediately persists the outbound row with `SentBy=Bot` + `ExternalMessageId` (`IWhatsAppOutboundRecorder`).
3. Echo with the same `ExternalMessageId` is treated as a duplicate and ignored (does not flip to Human).
4. `ConversationBotPolicy.CanAutoReply` is false for `BotMode.Human` / `Paused` (blocks future auto-replies once Phase 5 exists).

**Race window:** Between connector `markBotSent` and DB record, echoes still carry `botOriginated=true` while the connector process is up. Residual risk if ASP.NET crashes after send but before record **and** the connector restarts before the echo — false Human takeover. Acceptable for Phase 4.5; live test should confirm send→record ordering.

**Limitation:** Live phone send/receive still required before claiming production readiness.

---

## Media capability (discovery only)

Image events expose: message id, sender, timestamp, mimeType, fileLength, `downloadable` flag.  
Full download → object storage is **Phase 7**. Not implemented here.

---

## Security

- Loopback-only connector bind (`127.0.0.1`)
- Shared secret on all internal routes (constant-time compare on API)
- Session dir gitignored
- Logs redact QR/creds/message bodies (connector pino redaction; API avoids logging content)
- **Not** a public internet webhook without TLS + secret rotation + firewall

---

## Failure handling

| Failure | Behavior |
|---------|----------|
| Connector crash | API stays up; status Disconnected |
| ASP.NET down | Connector logs webhook failure; WhatsApp still works on phone |
| Session invalid | AUTHENTICATION_REQUIRED + QR |
| Duplicate event | Unique `ExternalMessageId` + handler short-circuit |
| Unsupported type | Persisted as `Other` when phone present |

---

## Account / ToS risk (mandatory)

This architecture automates a personal WhatsApp account via an unofficial protocol client. It may cause account restriction, session invalidation, or breakage when Meta changes WhatsApp. It is **not** WhatsApp Business Platform and is **not** officially supported by Meta.

---

## Automated test results (executed)

- Unit: mapper / fromMe / connection status — PASS  
- Integration (Testcontainers Postgres): webhook auth, persist once, technician fromMe → Human mode, connection state — PASS  
- Node messageMap smoke — PASS  

## Real WhatsApp tests

| Test | Result |
|------|--------|
| Connector starts / HTTP status | Verified (loopback + secret) |
| QR generation path | Verified (`AUTHENTICATION_REQUIRED`; QR only on interactive TTY) |
| Halil QR scan + linked session | **PENDING** — requires Halil on an interactive local terminal |
| Session reconnect without QR | **PENDING** (after first successful scan) |
| Inbound text → API → DB | **PENDING** live; **PASS** via webhook integration tests |
| Outbound text | **PENDING** live |
| fromMe / manual detection | **PENDING** live; **PASS** mapping/unit + webhook tests |
| Duplicate event | **PASS** (integration) |
| Image metadata shape | Mapping supports Image + downloadable flag; live image **PENDING** |

**STATUS for live account linkage: PARTIAL** until Halil completes QR pairing and controlled send/receive on a test chat.

---

## Pre-live hardening (Phase 4.5.1)

- Outbound bot messages persisted for echo correlation after connector restart  
- Connector secret compare uses `crypto.timingSafeEqual`  
- Session dir created with mode `0o700`  
- Baileys logger silenced; QR only on interactive TTY  
- Connector HTTP stays up if WA start fails; ASP.NET send returns 503 if connector offline  
- Pinned Baileys `@whiskeysockets/baileys@6.7.24` (lockfile must match)

## Live event observability (Phase 4.5.2)

Connector logs metadata only (no message bodies):

| Log event | When |
|-----------|------|
| `BAILEYS_MESSAGE_RECEIVED` | `messages.upsert` passed filters |
| `WEBHOOK_FORWARD_STARTED` | Before POST to ASP.NET |
| `EVENT_FORWARD_SUCCESS` | ASP.NET returned 2xx |
| `BAILEYS_UPSERT_FILTERED` | Upsert type not `notify`/`append` |

Fields: `messageId`, `fromMe`, redacted `remoteJid`, `messageType`, `botOriginated`, `httpStatus`, `durationMs`.

Restart connector after deploy to pick up logging changes.

## Known limitations

- Image send / media download pipeline not implemented  
- No AI / workflow  
- Companion device may conflict with Halil’s browser WhatsApp Web  
- Connector `botSentIds` is process-local; recovery depends on PostgreSQL outbound record  
- Real WhatsApp pairing / live fromMe confirmation still PENDING
