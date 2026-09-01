# WhatsApp AI Technical Support Bot — Phase 1 Architecture Audit

**Status:** Planning only — no production implementation  
**Date:** 2026-08-31  
**Audience:** Halil (product owner) + engineering  
**Repo state:** Greenfield (`led-sup-bot` has no application code yet)

---

## Document convention

| Label | Meaning |
|--------|---------|
| **CONFIRMED** | Required by product brief or verified against official WhatsApp/Meta platform constraints |
| **ASSUMPTION** | Reasonable default for planning; must be validated before build |
| **DECISION REQUIRED** | Halil / business must choose before or during early implementation |

---

## A vs B — WhatsApp automation approach (mandatory)

### A) Personal WhatsApp account automation

Examples: WhatsApp Web reverse-engineering clients, browser automation, unofficial libraries that mimic a phone/session.

| Aspect | Reality |
|--------|---------|
| Official status | **Not** supported by Meta for business automation |
| Terms of service | Violates WhatsApp ToS; account ban / number loss risk |
| Reliability | Breaks when WhatsApp Web / client protocol changes |
| Compliance | Poor fit for customer PII, media, support audit trails |
| Media / delivery | Fragile; no guaranteed webhooks, statuses, or SLAs |
| Production fitness | **Rejected as default production architecture** |

**CONFIRMED:** Do not design the production system around personal WhatsApp automation.

### B) WhatsApp Business Platform / Cloud API (recommended)

Official Meta product: WhatsApp Business Account (WABA) + Meta App + Cloud API + webhooks.

| Aspect | Reality |
|--------|---------|
| Official status | Supported production API (Cloud API; On-Premises API deprecated) |
| Messaging | Text, images, documents, templates, delivery status webhooks |
| Media | Media ID → resolve URL → download with access token |
| Security | App secret signature (`X-Hub-Signature-256`), verify token, optional mTLS |
| Scaling | Designed for business messaging volumes |
| Production fitness | **Required default for this product** |

**CONFIRMED decision for this project:** Use **WhatsApp Business Platform / Cloud API only**.

---

# 1. SYSTEM ARCHITECTURE

## 1.1 High-level components

```
┌─────────────┐     ┌──────────────────────┐     ┌─────────────────────┐
│  Customer   │────▶│ WhatsApp (Meta)      │────▶│ Webhook (HTTPS)     │
│  WhatsApp   │◀────│ Cloud API            │◀────│ ASP.NET Core API    │
└─────────────┘     └──────────────────────┘     └──────────┬──────────┘
                                                            │
                    ┌───────────────────────────────────────┼───────────────────────────┐
                    │                                       ▼                           │
                    │  ┌──────────────┐  ┌─────────────────────┐  ┌──────────────────┐ │
                    │  │ Conversation │  │ Workflow Engine     │  │ OpenAI           │ │
                    │  │ State Store  │◀▶│ (rules + slots)     │◀▶│ Extraction/NLG   │ │
                    │  └──────┬───────┘  └──────────┬──────────┘  └──────────────────┘ │
                    │         │                     │                                    │
                    │         ▼                     ▼                                    │
                    │  ┌──────────────┐  ┌─────────────────────┐  ┌──────────────────┐ │
                    │  │ PostgreSQL   │  │ Media Storage       │  │ Admin Dashboard  │ │
                    │  │ (Supabase)   │  │ (object store)      │  │ (technicians)    │ │
                    │  └──────────────┘  └─────────────────────┘  └──────────────────┘ │
                    └───────────────────────────────────────────────────────────────────┘
```

## 1.2 Inbound message flow (customer → bot)

1. Customer sends text/image on WhatsApp.
2. Meta delivers a webhook POST to the public HTTPS endpoint.
3. ASP.NET Core:
   - Verifies `X-Hub-Signature-256` (app secret).
   - Returns **HTTP 200 quickly** (ack first; process async — Meta retries on non-200).
   - Enqueues work keyed by WhatsApp message ID (idempotent).
4. Worker loads/creates `Customer` + active `Conversation`.
5. If conversation is in **HUMAN_HANDOFF** or bot is **OFF** → persist message, notify dashboard, **do not auto-reply**.
6. Otherwise:
   - Persist raw `Message`.
   - If media → download & store (see §7).
   - Call AI extraction with current slot schema + recent history.
   - Workflow engine merges extractions slots, validates, advances state.
   - Generate reply (template + AI phrasing under engine constraints).
   - Send via Cloud API `POST /{phone-number-id}/messages`.
7. Persist outbound message + delivery status updates from later webhooks.

## 1.3 Outbound response path

`ASP.NET Core` → Graph API (Bearer token) → WhatsApp → Customer.

Statuses (`sent` / `delivered` / `read` / `failed`) arrive as separate webhook events and update `Message.DeliveryStatus`.

## 1.4 Admin / technician visibility

```
WhatsApp traffic
    → same DB as bot
    → Admin Dashboard (authenticated)
        - live conversation list
        - message timeline
        - media gallery
        - case summary
        - Bot ON/OFF / Takeover controls
```

**ASSUMPTION:** Technicians work primarily from the admin dashboard (not only WhatsApp Business App).  
**DECISION REQUIRED:** Whether technicians also keep the WhatsApp Business App open via **Coexistence** (same number on App + Cloud API), or use **Cloud API-only** number + dashboard only.

## 1.5 Completion → summary → dashboard

When all required slots are filled and validated:

1. Workflow → `HUMAN_HANDOFF`.
2. System generates structured `TechnicianSummary` (deterministic template from DB fields; AI may draft narrative only as optional supplement).
3. Case status → `ReadyForTechnician`.
4. Dashboard notification (in-app; optional email/Telegram later).
5. Bot auto-replies stop for that conversation until bot is re-enabled.

---

# 2. WHATSAPP INTEGRATION

## 2.1 Required Meta / WhatsApp accounts

| Asset | Purpose |
|-------|---------|
| Meta Business Manager / Business Portfolio | Owns business assets |
| WhatsApp Business Account (WABA) | Container for phone numbers & templates |
| Meta Developer App | Webhooks, tokens, Graph API access |
| Business phone number | Customer-facing WhatsApp identity |
| System user + permanent token (or token refresh strategy) | Server-to-server auth |
| Display name review | Customer-visible business name |

**CONFIRMED:** Production requires a real WABA + Cloud API phone number — not a personal consumer WhatsApp account.

## 2.2 Business account requirements

- Business verification strongly recommended for higher limits / trust (timing depends on Meta review).
- Payment method on Meta for conversation-based billing.
- App in **Live** mode for production webhooks/messages.
- Permissions typically include messaging-related WhatsApp permissions configured on the app / system user.

## 2.3 Phone number requirements

Possible starting states (must be discovered — **do not assume**):

| Current number type | Implication |
|---------------------|-------------|
| Personal WhatsApp (consumer) | Cannot safely automate officially. Must migrate customers to a **Business** number on Cloud API, or convert path carefully with downtime. |
| WhatsApp Business **App** only | May onboard to Cloud API via **Coexistence** (App + API, history sync) if eligible; eligibility often needs active usage. Or fully migrate to Cloud API (App usage rules change). |
| Already on Cloud API / BSP | Reuse / migrate WABA; point webhooks to our backend. |
| New dedicated support number | Cleanest technically; marketing/ops must publish new number. |

**DECISION REQUIRED (Halil):**

1. What is the current company number? (personal / Business App / already API)
2. Keep same number vs new support number?
3. Coexistence (App + API) vs Cloud-API-only + admin dashboard?

**ASSUMPTION for architecture:** Target a Cloud API–connected business number. Prefer **Cloud-API-only + dashboard** for clear bot/human control, unless ops insists on App coexistence.

## 2.4 Webhook requirements

| Requirement | Detail |
|-------------|--------|
| Public HTTPS URL | e.g. `https://api.example.com/webhooks/whatsapp` |
| GET verification | Compare `hub.verify_token`; return `hub.challenge` |
| POST events | `messages`, `message_status` (and account fields as needed) |
| Fast 200 | Ack within seconds; process async |
| Signature | Validate `X-Hub-Signature-256` with App Secret |
| Idempotency | Dedupe by WhatsApp `message.id` |
| Optional mTLS | Stronger inbound trust; track Meta CA updates if enabled |

## 2.5 Authentication / tokens

| Secret | Use |
|--------|-----|
| Webhook verify token | One-time/challenge setup (server-known string) |
| App Secret | HMAC signature verification |
| Access token (system user preferred) | Send messages, download media |
| Phone Number ID | Outbound API path |
| WABA ID | Asset management |

Store in Azure Key Vault / environment secrets — never in git.

## 2.6 Incoming message handling

Webhook payload contains arrays (`entry` → `changes` → `value` → `messages[]`). Handle:

- Text (`text.body`)
- Image (`image.id`, caption)
- Document / other types (policy: accept images for labels; politely reject unsupported for this flow)
- Reply / context IDs if present
- Batched messages in one POST

## 2.7 Outgoing message handling

- Within **24-hour customer service window** after last customer message: free-form text/image replies allowed.
- Outside window: only **pre-approved message templates** (for re-engagement).
- Upload media for config example image via Media Upload API or host a public HTTPS link Meta can fetch.
- Rate limits / quality rating must be monitored in production.

## 2.8 Image / media handling

1. Webhook provides `media_id` (not file bytes).
2. `GET /{media-id}` → temporary URL (~5 minutes).
3. Download with `Authorization: Bearer {token}`.
4. Persist to own storage immediately.
5. Media IDs expire (order of days on Meta side) — durable storage is mandatory for cases/audit.

## 2.9 Message delivery status

Subscribe to status webhooks; map to internal message records. Surface failures in dashboard (failed delivery → technician awareness).

## 2.10 Production considerations

- Idempotent webhook processing
- Queue + worker isolation from HTTP
- Template approval lead time for out-of-window messages
- Conversation pricing / analytics in Meta
- Display name & quality rating
- Staging app + test numbers before production cutover
- Logging without storing full tokens or unnecessary PII

**Can the existing company WhatsApp number be used?**  
**Unknown until Halil confirms number type.** Architecture supports all three paths (new number, coexistence, full API migration) without changing core app design — only onboarding differs.

---

# 3. CONVERSATION STATE MACHINE

## 3.1 States

| State | Purpose |
|-------|---------|
| `START` | New conversation created |
| `GREETING` | Acknowledge hello; set expectations |
| `ASK_SCREEN_SIZE` | Need width & height (cm) |
| `ASK_PANEL_LABEL_PHOTOS` | Need ≥1 (prefer all) back-label photos |
| `ASK_RECEIVER_COUNT` | Need receiver card count |
| `ASK_CONFIG_CONNECTION` | Ask to connect controller via config/printer cable |
| `SEND_CONFIG_EXAMPLE` | Send example image of correct connection |
| `ASK_ANYDESK` | Ask to open AnyDesk + provide ID |
| `COLLECTING_FINAL_INFO` | Soft state: missing optional clarifications |
| `HUMAN_HANDOFF` | Bot silent; technician owns thread |
| `COMPLETED` | Case closed / resolved |

Internal control flags (not user-visible states):

- `BotMode`: `Active` | `Paused` | `HumanTakeover`
- `RequiredSlots` completion bitmap

## 3.2 Slot-driven progression (not rigid scripts)

**CONFIRMED:** The bot must not blindly ask every question in order if answers already arrived.

Engine maintains **required slots**:

| Slot | Required | Source |
|------|----------|--------|
| `screenWidthCm` | Yes | text |
| `screenHeightCm` | Yes | text |
| `panelLabelPhotos` | Yes (≥ N photos) | images |
| `receiverCount` | Yes | text |
| `configCableConnected` | Yes (customer confirmation) | text/intent |
| `configExampleSent` | Yes (system action) | system |
| `anydeskId` | Yes | text |
| `customerPhone` | Yes | WhatsApp `from` |

**How current state is determined:**

```
effectiveState = first incomplete required stage in priority order
```

Priority order (example):

1. Greeting acknowledged (once)
2. Screen size complete
3. Label photos sufficient
4. Receiver count
5. Config connection confirmed
6. Config example sent
7. AnyDesk ID
8. → HUMAN_HANDOFF

So a customer who says *"Merhaba, ekran 500x300 ve 8 tane receiver var"* yields:

- Extract: greeting, width=500, height=300, receivers=8
- Skip `ASK_SCREEN_SIZE` and `ASK_RECEIVER_COUNT`
- Next ask: panel label photos (and send config example later when that stage is reached)

## 3.3 Multi-answer single message

1. AI returns structured extraction for **all** detectable slots + confidence.
2. Engine applies only high-confidence validated fields.
3. Ambiguous fields → clarification questions (do not guess).
4. Response generator asks **only for the next missing high-priority item(s)** (prefer one primary ask; optionally acknowledge what was understood).

## 3.4 Transitions

| From | Trigger | To |
|------|---------|-----|
| START | inbound message | GREETING or slot-appropriate ask |
| any collecting | slot filled | next incomplete |
| all required filled | auto | HUMAN_HANDOFF (+ summary) |
| any | admin takeover | HUMAN_HANDOFF |
| HUMAN_HANDOFF | admin re-enable bot | resume at incomplete slot state |
| HUMAN_HANDOFF | case closed | COMPLETED |

---

# 4. DATA MODEL

## 4.1 Entities (logical)

### Customer
- `Id` (uuid)
- `WhatsAppPhoneE164` (unique)
- `DisplayName` (optional, from profile if available)
- `CreatedAt`, `UpdatedAt`

### Conversation
- `Id`
- `CustomerId`
- `Channel` = WhatsApp
- `Status`: `Active` | `WaitingTechnician` | `InHumanSupport` | `Completed` | `Abandoned`
- `BotMode`: `Active` | `Paused` | `HumanTakeover`
- `CurrentState` (enum)
- `Language` (e.g. `tr`, `en`) — **ASSUMPTION:** Turkish primary
- `StartedAt`, `LastCustomerMessageAt`, `ClosedAt`
- `WhatsAppServiceWindowExpiresAt` (derived from last customer message + 24h)

### Message
- `Id`
- `ConversationId`
- `Direction`: `Inbound` | `Outbound`
- `WhatsAppMessageId` (unique, nullable for outbound until ack)
- `Type`: `Text` | `Image` | `Document` | `Template` | `System`
- `Body`
- `MediaAssetId` (nullable)
- `DeliveryStatus`
- `SentBy`: `Bot` | `Technician` | `System`
- `TechnicianUserId` (nullable)
- `CreatedAt`
- `RawPayloadRef` (optional encrypted/blob pointer — minimize retention)

### TechnicalSupportCase
- `Id`
- `ConversationId` (1:1 or 1:active)
- `Status`: `Collecting` | `ReadyForTechnician` | `InProgress` | `Resolved`
- `SummaryJson` / `SummaryMarkdown`
- `AssignedTechnicianId`
- `ReadyAt`, `ResolvedAt`

### ScreenInformation
- `CaseId`
- `WidthCm` (decimal)
- `HeightCm` (decimal)
- `RawUtterance`
- `Confidence`
- `Validated` (bool)

### PanelLabelPhoto
- `Id`
- `CaseId`
- `MediaAssetId`
- `SortOrder`
- `VisionAnalysisJson` (nullable)
- `ReceivedAt`

### ReceiverInformation
- `CaseId`
- `Count` (int)
- `RawUtterance`
- `Confidence`
- `Validated`

### AnyDeskInformation
- `CaseId`
- `AnyDeskId` (string, normalized digits)
- `RawUtterance`
- `Confidence`
- `Validated`

### ConversationState (history / audit)
- `Id`
- `ConversationId`
- `FromState`, `ToState`
- `Reason`
- `CreatedAt`

### AIExtractionResult
- `Id`
- `MessageId`
- `Model`
- `PromptVersion`
- `InputHash`
- `OutputJson` (structured)
- `ConfidenceMap`
- `LatencyMs`
- `CreatedAt`
- **Do not** log full system prompts with secrets; redact PII in operational logs as policy requires

### MediaAsset
- `Id`
- `StorageProvider`
- `Bucket` / `Path`
- `MimeType`
- `SizeBytes`
- `Sha256`
- `Source`: `WhatsAppInbound` | `OutboundStatic`
- `WhatsAppMediaId` (nullable)
- `CreatedAt`

### AdminUser
- `Id`, `Email`, `Role` (`Admin` | `Technician`), auth subject id

### ConversationAssignment / events (optional)
- Takeover events, bot toggles, audit trail

## 4.2 Relationships

```
Customer 1──* Conversation 1──1 TechnicalSupportCase
Conversation 1──* Message
Conversation 1──* ConversationState
TechnicalSupportCase 1──0..1 ScreenInformation
TechnicalSupportCase 1──* PanelLabelPhoto
TechnicalSupportCase 1──0..1 ReceiverInformation
TechnicalSupportCase 1──0..1 AnyDeskInformation
Message 0..1──0..1 AIExtractionResult
Message 0..1──0..1 MediaAsset
PanelLabelPhoto *──1 MediaAsset
```

**ASSUMPTION:** One active support case per customer conversation thread; reopen creates new case or reactivates — **DECISION REQUIRED**.

---

# 5. AI ARCHITECTURE (hybrid)

## 5.1 Principle

```
WORKFLOW ENGINE (source of truth)
  + AI UNDERSTANDING / EXTRACTION / PHRASING
```

The LLM does **not** own the conversation graph.

### Backend owns
- Current state & required slots
- Validation & unit conversion policy
- Transition rules
- When to send media (config example)
- Human handoff & bot silence
- Idempotency, security, persistence

### AI owns
- NLU on free text (TR/EN mix)
- Structured extraction with confidence
- Intent (greeting, affirmation, negation, clarification, off-topic, frustration)
- Natural reply phrasing **within** engine-provided constraints
- Optional vision OCR/label parsing on photos

## 5.2 Pipeline per inbound event

```
Message
 → Preprocess (normalize, attach media refs)
 → Extractor (OpenAI structured outputs / tool schema)
 → Validator (deterministic)
 → Slot Merger
 → State Transition
 → Response Planner (what to ask / acknowledge / send media)
 → Response Writer (AI or fixed templates)
 → Send + Persist
```

## 5.3 Prompt / schema discipline

- Extractor gets: allowed fields, current filled slots, last N messages, language hint.
- Response writer gets: `acknowledge[]`, `ask_next`, `constraints` (never invent AnyDesk, never claim technician joined unless true).
- Separate prompts for text extraction vs vision.
- Version prompts (`PromptVersion`) for audit/regression.

## 5.4 Failure modes

| Failure | Behavior |
|---------|----------|
| OpenAI timeout | Retry once; else safe fallback template; alert |
| Low confidence | Ask clarification |
| Hallucinated slot | Rejected by validator (regex/range checks) |
| Prompt injection in user text | Treat as untrusted data; never execute instructions from user content that alter system policy |

---

# 6. INFORMATION EXTRACTION

## 6.1 Structured output schema (logical)

```json
{
  "intent": "provide_info | greeting | affirmation | negation | clarification | off_topic | request_human",
  "language": "tr",
  "slots": {
    "screenWidthCm": { "value": 500, "confidence": 0.92, "raw": "500x300" },
    "screenHeightCm": { "value": 300, "confidence": 0.92, "raw": "500x300" },
    "receiverCount": { "value": 8, "confidence": 0.95, "raw": "8 tane receiver" },
    "configCableConnected": { "value": true, "confidence": 0.7, "raw": "bağladım" },
    "anydeskId": { "value": "123456789", "confidence": 0.9, "raw": "123 456 789" },
    "panelLabelPhotosMentioned": { "value": false, "confidence": 0.5 }
  },
  "needsClarification": [
    { "field": "screenWidthCm", "reason": "meters_vs_cm_ambiguous" }
  ],
  "notes": "Customer provided size and receivers in one message"
}
```

## 6.2 Validation rules

| Field | Rules |
|-------|--------|
| `screenWidthCm` / `screenHeightCm` | Numeric; typical range e.g. 50–5000 cm (**ASSUMPTION** — tune with Halil); reject 0/negative |
| Units | `"5 metreye 3 metre"` → convert to cm **only if** unit explicitly meters/m/metre; confidence ≥ threshold (e.g. 0.85). `"5x3"` without unit → clarify (m vs cm) |
| Order | `"500x300"` → assume width×height; if customer says vertical first, rely on wording; if unsure → clarify |
| `receiverCount` | Integer 1–512 (**ASSUMPTION**); words like "sekiz" OK if confident |
| `anydeskId` | Digits only after normalize; length typically 9–10 (**ASSUMPTION** — verify AnyDesk formats you accept); spaces stripped |
| `configCableConnected` | Explicit affirmation required; vague "sanırım" → clarify |
| Photos | Count of stored `PanelLabelPhoto`; captions alone do not satisfy |

## 6.3 Ambiguity policy

**CONFIRMED:** If confidence &lt; threshold or validator fails → ask clarification; **never guess** critical support parameters.

Example clarification:  
*"Ekranı 5×3 metre (500×300 cm) olarak mı kaydedelim, yoksa 5×3 cm mi?"*

---

# 7. PHOTO HANDLING

## 7.1 Pipeline

```
WhatsApp image webhook
  → media_id
  → GET media metadata + URL
  → download bytes (Bearer token)
  → virus/size/mime checks
  → encrypt-at-rest object storage
  → MediaAsset row
  → PanelLabelPhoto linked to case
  → enqueue VisionAnalysisJob
  → OpenAI Vision / OCR structured extract (optional fields)
  → store VisionAnalysisJson
  → update "photos sufficient?" rule
```

## 7.2 Multi-photo policy

- Accept **many** photos until customer says done **or** technician marks sufficient.
- Minimum gate: **≥ 1** photo before leaving `ASK_PANEL_LABEL_PHOTOS` (**ASSUMPTION** — Halil may require “all labels” = N≥2 or explicit customer confirmation “hepsini gönderdim”).
- **DECISION REQUIRED:** Exit criteria for label photos (count vs customer confirmation vs vision completeness).

## 7.3 Vision role

Vision **assists** (model, serial, voltage clues) but does **not** replace collecting photos for the technician. Summary always lists photo count + thumbnails.

## 7.4 Outbound example image

Static asset in storage or repo-managed media; send as image message when entering `SEND_CONFIG_EXAMPLE`. Track `configExampleSent=true`.

---

# 8. HUMAN HANDOFF

## 8.1 Automatic handoff

When required slots validated:

1. Generate technician summary.
2. Set `Conversation.CurrentState = HUMAN_HANDOFF`.
3. Set `BotMode = HumanTakeover`.
4. Set case `ReadyForTechnician`.
5. Notify dashboard.
6. Optional customer message: *"Bilgileriniz alındı, teknisyenimiz kısa süre içinde ilgilenecek."*
7. Subsequent inbound messages: store + notify; **no bot reply**.

## 8.2 Manual Bot ON/OFF / Takeover (admin)

| Control | Effect |
|---------|--------|
| **Take over** | BotMode=HumanTakeover; stop auto-replies for that conversation |
| **Pause bot (global)** | All new/active conversations stop auto-replies |
| **Resume bot** | BotMode=Active; continue from incomplete slots (do not re-ask filled slots) |
| **Send as technician** | Outbound via Cloud API as same business number; tagged `SentBy=Technician` |

## 8.3 Race conditions

If customer messages while summary is generating: still hand off; include late messages in thread. Use row version / state machine locks per conversation id.

---

# 9. TECHNICIAN SUMMARY

Deterministic render from DB (preferred over free-form LLM as source of truth):

```
CUSTOMER
Phone: +90...
Conversation: {id}
Language: tr

SCREEN
Width: 384 cm
Height: 216 cm
Source: "384x216" (confidence 0.94)

PANEL LABELS
4 photos received
[links/thumbnails]

RECEIVERS
12 units

CONFIG CONNECTION
Customer confirmed: Yes
Example image sent: Yes

ANYDESK
123456789

STATUS
Ready for technician
Collected at: 2026-08-31T13:05:00Z
```

Optional AI narrative section (clearly labeled “AI notes — verify”): unusual customer comments, vision OCR snippets.

Dashboard shows the same structure + full chat history.

---

# 10. ADMIN DASHBOARD

## 10.1 Recommended stack (planning)

**ASSUMPTION:** ASP.NET Core API + separate SPA (React/Next) **or** Blazor — **DECISION REQUIRED**.  
Prefer React/Next admin if Halil wants modern UX; Blazor if single .NET team preference.

## 10.2 Views / capabilities

| Area | Features |
|------|----------|
| Queue | Active / Waiting / In human support / Completed filters |
| Conversation detail | History, phone, slots, photos, AnyDesk, bot mode toggle |
| Case panel | Structured summary, assign technician, mark resolved |
| Media | Secure authenticated image viewer |
| Controls | Per-conversation takeover; global bot pause |
| Search | Phone, AnyDesk, date, status |
| Auth | Login, roles Admin/Technician |

## 10.3 Real-time

**ASSUMPTION:** SignalR for live message updates in dashboard.

---

# 11. SECURITY

| Risk | Mitigation |
|------|------------|
| WhatsApp access tokens | Key Vault; system user token; rotation; never client-side |
| OpenAI API keys | Server-only; Key Vault; spend limits / monitoring |
| Customer phone numbers | Encrypt at rest where feasible; minimize logs; access control |
| AnyDesk IDs | Treat as sensitive remote-access secrets; role-based view; audit access |
| Uploaded images | Private bucket; signed short-TTL URLs; authz checks; malware scan |
| Webhook forgery | `X-Hub-Signature-256` constant-time compare; verify token; optional mTLS |
| Admin auth | OIDC / Entra ID / strong auth; MFA for admins |
| Authorization | Conversation-level ACLs; technicians only see assigned or all-support scope |
| Prompt injection | User content is data; fixed system policy; no tool that executes user instructions; validate outputs |
| AI hallucination | Deterministic validators; never trust model for handoff readiness alone |
| Sensitive data in logs | Redact phones, AnyDesk, tokens; structured logging policy |
| SSRF via media URLs | Only download Meta media hosts; allowlist |
| Retention | Define retention for media/messages (**DECISION REQUIRED**) |

---

# 12. DEVELOPMENT PHASE PLAN

Each phase follows: **AUDIT → IMPLEMENT → TEST → FIX → RETEST → VERIFY → REPORT**.

| Phase | Name | Goal |
|-------|------|------|
| **1** | Architecture | This document — review & decisions |
| **2** | Project foundation | Solution structure, CI, config, secrets pattern, logging, health checks |
| **3** | Database | PostgreSQL schema, migrations, repositories |
| **4** | WhatsApp integration | Webhook verify/signature, inbound queue, outbound send, status, media download (official API only) |
| **5** | Conversation engine | Slots, state machine, multi-slot merge, handoff flags (rules without AI first) |
| **6** | AI extraction | Structured OpenAI extraction + validators + response planner |
| **7** | Photo / vision | Storage, multi-photo rules, vision job, config example send |
| **8** | Human handoff | Auto + manual takeover, bot silence, technician outbound |
| **9** | Admin dashboard | Queues, detail, summary, media, controls, auth |
| **10** | End-to-end testing | Scripted TR scenarios (multi-answer, ambiguity, photos, handoff) |
| **11** | Production validation | Meta Live, real number, templates, monitoring, runbook |

### Adjusted note from audit

Insert a **Phase 2.5 — Meta account & phone-number onboarding** (parallel to foundation): WABA, number path, display name, test numbers. WhatsApp code (Phase 4) is blocked without this.

Optional later: BSP (360dialog, etc.) if Halil prefers partner billing — architecture stays the same with an adapter interface `IWhatsAppClient`.

---

# Confirmed vs assumptions vs decisions

## Confirmed requirements

- Automate LED support intake on WhatsApp via **official Cloud API**.
- Hybrid workflow engine + AI extraction (not LLM-only control).
- Collect: screen size (cm), label photos (multiple), receivers, config cable + example image, AnyDesk, then human handoff + structured summary.
- Multi-fact utterances must not re-ask known slots.
- Ambiguity → clarify, don’t guess.
- Admin visibility + bot on/off / takeover.
- ASP.NET Core backend, OpenAI, relational DB, admin dashboard.
- No production code in Phase 1; no unofficial WhatsApp as default.

## Assumptions (validate)

- Primary language Turkish.
- PostgreSQL via Supabase or Azure Database for PostgreSQL.
- Object storage for media (S3-compatible / Azure Blob / Supabase Storage).
- Dashboard SPA + SignalR.
- Photo minimum ≥1 + customer “done” confirmation.
- Screen size range and AnyDesk ID length heuristics above.
- Cloud-API-only number preferred over coexistence unless ops requires App.

## Decisions required from Halil

1. Current WhatsApp number type and keep-vs-replace strategy.
2. Coexistence (Business App + API) vs API-only + dashboard.
3. Meta Business Manager ownership / who creates WABA & billing.
4. Hosting: Azure vs other; Supabase vs Azure PostgreSQL.
5. Admin UI: React/Next vs Blazor.
6. Auth provider for technicians.
7. Label photo completion rule.
8. Support hours / SLA messaging to customers.
9. Data retention (messages, photos, AnyDesk).
10. Whether bot may answer off-topic FAQ or always stay on intake script.
11. Branding / WhatsApp display name.
12. Need for message templates (after-hours re-open).

---

# AUDIT RESULT

### Architecture status
**Complete for Phase 1.** Greenfield repo; no code to reverse-audit. Target architecture is sound: **Cloud API + ASP.NET Core + slot/state workflow + OpenAI extraction + PostgreSQL + media store + admin handoff**.

### Major risks
1. **Phone number / Meta onboarding** delays (eligibility, verification, display name).
2. Treating unofficial WhatsApp libs as a shortcut → ban / outage (**avoid**).
3. AI guessing dimensions/units → wrong technician prep (**mitigate with validators**).
4. Media URL expiry if download is deferred.
5. Bot continuing after handoff (race) → confused customers.
6. Secret leakage in logs/repo.
7. 24h window / templates if technicians need to re-initiate chats.

### Decisions required from Halil
See list above — **especially items 1–3 and 7**.

### Recommended technology stack
| Layer | Recommendation |
|-------|----------------|
| WhatsApp | Meta Cloud API (direct); `IWhatsAppClient` abstraction |
| Backend | ASP.NET Core 8/9, background queue (Channel/Hangfire/Azure Queue) |
| AI | OpenAI API (structured outputs); separate vision calls |
| DB | PostgreSQL (Supabase or Azure) + EF Core |
| Media | Azure Blob or Supabase Storage (private) |
| Admin | React or Next.js + SignalR (**or** Blazor per team preference) |
| Auth | Microsoft Entra ID / Auth0 / Supabase Auth — pick with hosting |
| Hosting | Azure App Service / Container Apps + Key Vault |
| Observability | OpenTelemetry + structured logs (redacted) |

### Recommended next phase
**Phase 2 — Project foundation** + **parallel Meta/WABA phone-number audit with Halil** (Phase 2.5).  
Do **not** start Phase 4 WhatsApp coding until number strategy is chosen and a Meta test app exists.

### Files that should be created in the next implementation phase

```
/LedSupBot.sln
/src/LedSupBot.Api/                 # ASP.NET Core host, webhooks, auth
/src/LedSupBot.Application/         # workflow, handlers, DTOs
/src/LedSupBot.Domain/              # entities, state enums, slot model
/src/LedSupBot.Infrastructure/      # EF Core, storage, OpenAI, WhatsApp client
/src/LedSupBot.Admin/               # dashboard frontend (or /apps/admin)
/tests/LedSupBot.UnitTests/
/tests/LedSupBot.IntegrationTests/
/docs/FAZ1-ARCHITECTURE-AUDIT.md    # (this file — already created)
/docs/DECISIONS.md                  # ADR log for Halil decisions
/.env.example                       # placeholder names only, no secrets
/README.md                          # setup overview
```

---

**STOP:** Do not begin implementation until this report is reviewed and key Halil decisions (especially WhatsApp number path) are recorded.
