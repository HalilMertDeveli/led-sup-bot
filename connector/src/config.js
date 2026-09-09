import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { resolveSessionDirectory } from "./sessionStore.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");

function loadDotEnv() {
  const envPath = path.join(root, ".env");
  if (!fs.existsSync(envPath)) return;
  for (const line of fs.readFileSync(envPath, "utf8").split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const idx = trimmed.indexOf("=");
    if (idx <= 0) continue;
    const key = trimmed.slice(0, idx).trim();
    const value = trimmed.slice(idx + 1).trim();
    if (!(key in process.env)) {
      process.env[key] = value;
    }
  }
}

loadDotEnv();

function required(name, fallback) {
  const value = process.env[name] ?? fallback;
  if (!value) {
    throw new Error(`Missing required env ${name}`);
  }
  return value;
}

/**
 * Shared secret must match ASP.NET WhatsAppConnector:SharedSecret.
 * Canonical env: WHATSAPP_CONNECTOR_SHARED_SECRET
 * Alias (connector .env): CONNECTOR_SHARED_SECRET
 * No empty secret; no hard-coded production default.
 */
function resolveSharedSecret() {
  const primary = process.env.WHATSAPP_CONNECTOR_SHARED_SECRET?.trim();
  const alias = process.env.CONNECTOR_SHARED_SECRET?.trim();
  const value = primary || alias;
  if (!value) {
    throw new Error(
      "Missing CONNECTOR_SHARED_SECRET or WHATSAPP_CONNECTOR_SHARED_SECRET (must match ASP.NET)",
    );
  }
  return value;
}

export const config = {
  port: Number(process.env.PORT || 3100),
  sharedSecret: resolveSharedSecret(),
  aspNetWebhookUrl: required(
    "ASPNET_WEBHOOK_URL",
    "http://127.0.0.1:5080/internal/whatsapp/events",
  ),
  sessionDir: resolveSessionDirectory(root, process.env.SESSION_DIR),
  logLevel: process.env.LOG_LEVEL || "info",
};
