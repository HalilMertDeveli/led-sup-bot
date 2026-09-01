# LedSupBot WhatsApp Connector (Baileys)

**Unofficial.** Not Meta-approved. Linked-device companion for a personal WhatsApp account. Carries ToS / ban / session risks.

## Quick start

```bash
cd connector
cp .env.example .env
# set CONNECTOR_SHARED_SECRET to the same value as ASP.NET WhatsAppConnector__SharedSecret
npm install
npm start
```

Scan the QR in the terminal with WhatsApp → Linked devices.

Session files land in `.sessions/` (gitignored). Never commit them.

## Endpoints (localhost)

All require header `X-Connector-Secret: <shared secret>` except process health if you add one later.

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/status` | Connection status |
| POST | `/send/text` | `{ "to": "905xxxxxxxxx", "text": "..." }` |
| POST | `/media/download` | `{ "messageId": "...", "remoteJid": "..." }` metadata probe |

Inbound events are POSTed to ASP.NET: `{ASPNET_WEBHOOK_URL}` with the same secret.
