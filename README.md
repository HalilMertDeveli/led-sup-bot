# LED Support Bot

WhatsApp AI technical support intake bot for LED display support workflows.

## Phase status

| Phase | Status |
|-------|--------|
| 1 Architecture | Complete |
| 2 Project foundation | Complete |
| 3 Database | Complete |
| 4 Personal WA audit | Complete |
| **4.5 WhatsApp connector** | **Baileys sidecar (unofficial) — see docs/FAZ4.5** |
| 5 Conversation engine | Not started |
| 6 OpenAI | **Not implemented** |
| 9 Admin dashboard | Foundation shell only |

**OpenAI / customer workflow automation are NOT implemented.**  
**WhatsApp connector is unofficial (Baileys linked-device). Not Meta-approved. ToS/ban risk.**

## Solution structure

```
LedSupBot.sln
src/ ... Asp.NET Core
connector/                 # Baileys Node sidecar (Phase 4.5)
admin/
docker-compose.yml
docs/
```

---

## Setup on a New Computer

Portable source backup. Clone this repository, recreate local configuration, and start services on a fresh Windows machine.

### Requirements

| Tool | Version / notes |
|------|-----------------|
| Git | Any recent version |
| .NET SDK | **8.0.424** (pinned in `global.json`; 8.0.x acceptable) |
| Node.js | **20+** (`connector/package.json` engines; CI uses Node 22 for admin) |
| npm | Bundled with Node.js |
| Docker Desktop | Required for local PostgreSQL (`docker-compose.yml`) and integration tests (Testcontainers) |
| PostgreSQL | Provided locally via Docker (`postgres:16-alpine` in `docker-compose.yml`) |
| `dotnet-ef` global tool | Required for EF Core migrations (`dotnet tool install --global dotnet-ef`) |

### Clone

```powershell
git clone <repository-url>
cd led-sup-bot
```

Replace `<repository-url>` with your GitHub remote after you create the repository (see [GIT](#git-portability) below).

### Backend configuration

The API reads **process environment variables** (not a `.env` file on disk). Use `.env.example` as the variable checklist.

```powershell
copy .env.example .env
```

Edit `.env` locally for your reference (`.env` is gitignored and never committed). Before running the API or EF commands, set the required variables in your PowerShell session. Minimum for local development:

```powershell
$env:DATABASE_CONNECTION_STRING = "Host=localhost;Port=5432;Database=led_sup_bot;Username=postgres;Password=postgres"
$env:ASPNETCORE_ENVIRONMENT = "Development"
```

Optional variables (placeholders in `.env.example`): `OPENAI_API_KEY`, WhatsApp Cloud API tokens, `STORAGE_CONNECTION_STRING`, connector settings (`WHATSAPP_CONNECTOR_*`), workflow flags.

For the Baileys connector, also set a shared secret in the API process (must match `connector/.env`):

```powershell
$env:WHATSAPP_CONNECTOR_SHARED_SECRET = "your-local-shared-secret"
$env:WHATSAPP_CONNECTOR_ENABLED = "true"
$env:WHATSAPP_CONNECTOR_BASE_URL = "http://127.0.0.1:3100"
```

### Connector configuration

```powershell
cd connector
copy .env.example .env
```

Edit `connector/.env` and configure:

| Variable | Purpose |
|----------|---------|
| `CONNECTOR_SHARED_SECRET` | Shared HMAC secret — **must match** API `WHATSAPP_CONNECTOR_SHARED_SECRET` |
| `WHATSAPP_CONNECTOR_SHARED_SECRET` | Alias for the same secret (optional if `CONNECTOR_SHARED_SECRET` is set) |
| `ASPNET_WEBHOOK_URL` | ASP.NET inbound webhook (default `http://127.0.0.1:5080/internal/whatsapp/events`) |
| `PORT` | Connector listen port (default `3100`) |
| `SESSION_DIR` | Local session folder (default `.sessions`, gitignored) |

```powershell
cd ..
```

### PostgreSQL (Docker)

Start the local database using the project's existing Docker Compose file:

```powershell
docker compose up -d
```

This starts `postgres:16-alpine` on port **5432** with database `led_sup_bot`, user `postgres`, password `postgres` (see `docker-compose.yml`). Data is stored in the Docker volume `led_sup_bot_pgdata` and is **not** committed to Git.

### Database (EF Core migrations)

Migrations live in `src/LedSupBot.Infrastructure/Persistence/Migrations/`. They are **not** applied automatically at startup.

With Postgres running and `DATABASE_CONNECTION_STRING` set:

```powershell
dotnet ef database update --project src/LedSupBot.Infrastructure --startup-project src/LedSupBot.Api
```

To add a new migration during development:

```powershell
dotnet ef migrations add <MigrationName> --project src/LedSupBot.Infrastructure --startup-project src/LedSupBot.Api
```

### API

From the repository root:

```powershell
$env:DATABASE_CONNECTION_STRING = "Host=localhost;Port=5432;Database=led_sup_bot;Username=postgres;Password=postgres"
dotnet run --project src/LedSupBot.Api --launch-profile http
```

API listens at **http://localhost:5080** (Swagger at `/swagger`).

### Connector

In a separate terminal:

```powershell
cd connector
npm install
npm.cmd start
```

On first run, scan the QR code in the terminal: **WhatsApp → Linked devices → Link a device**.

**WhatsApp session is local and intentionally NOT stored in GitHub.** The folder `connector/.sessions/` is gitignored. After cloning on another computer, start the connector and **pair the WhatsApp account again** using Linked Devices. The previous machine's session will not be available.

Controlled send test (not for customers):

```powershell
curl -X POST http://127.0.0.1:5080/internal/whatsapp/send-text `
  -H "Content-Type: application/json" `
  -H "X-Connector-Secret: your-local-shared-secret" `
  -d "{\"to\":\"YOUR_TEST_NUMBER\",\"text\":\"connector test\"}"
```

Details: `docs/FAZ4.5-WHATSAPP-CONNECTOR.md`

### Admin (React / Vite)

```powershell
cd admin
copy .env.example .env
npm install
npm run dev
```

Dev server: **http://localhost:5173**. Set `VITE_API_BASE_URL` in `admin/.env` (default `http://localhost:5080`).

Production build:

```powershell
npm run build
```

### Tests

From the repository root:

```powershell
# .NET restore, build, and all tests (unit + integration; integration needs Docker Desktop)
dotnet build LedSupBot.sln --configuration Release
dotnet test LedSupBot.sln --configuration Release --verbosity normal

# Connector unit tests
cd connector
npm test

# Admin production build
cd ..\admin
npm run build
```

Integration tests use **Testcontainers** with `postgres:16-alpine` — Docker must be running.

---

## GIT (portability)

This repository is prepared for GitHub backup. The following are **never** committed:

- `.env`, `connector/.env`
- `connector/.sessions/` (WhatsApp linked-device credentials)
- `node_modules/`, `bin/`, `obj/`
- Local database volumes / data

If no GitHub remote is configured yet, create a new repository on GitHub, then:

```powershell
git remote add origin https://github.com/<your-user>/<your-repo>.git
git push -u origin master
```

Use your actual repository URL. Do not force-push.

---

## Docs

- `docs/FAZ1-ARCHITECTURE-AUDIT.md`
- `docs/FAZ2-PROJECT-FOUNDATION.md`
- `docs/FAZ3-DATABASE.md`
- `docs/FAZ4-PERSONAL-WHATSAPP-AUDIT.md`
- `docs/FAZ4.5-WHATSAPP-CONNECTOR.md`
- `docs/DECISIONS.md`
