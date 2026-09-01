# LED Support — Admin Dashboard

Local-only technician operations panel (Phase 9).

## Start

1. PostgreSQL: `docker compose up -d` (from repo root)
2. API with admin auth env vars set (see root `.env.example`)
3. `npm.cmd run dev` in this folder

## Auth

- Login password: `ADMIN_DASHBOARD_PASSWORD` (API env only)
- Session: HttpOnly cookie signed with `ADMIN_SESSION_SECRET`
- Browser never receives connector secret or OpenAI key

## UI notes

- **Conversation list / case panel:** BOT / HUMAN / PAUSED badges (operator safety)
- **Chat:** normal 1:1 support appearance — inbound left, outbound right labeled **Sen** only; no BOT/TECHNICIAN/CUSTOMER per-message labels

## Scripts

| Command | Purpose |
|---------|---------|
| `npm.cmd run dev` | Vite dev server (:5173) |
| `npm.cmd run build` | Production bundle → `dist/` |
| `npm.cmd run test` | Vitest (message presentation) |
| `npm.cmd run lint` | Oxlint |
