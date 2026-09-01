import {
  DisconnectReason,
  downloadMediaMessage,
  fetchLatestBaileysVersion,
  makeWASocket,
  useMultiFileAuthState,
} from "@whiskeysockets/baileys";
import { Boom } from "@hapi/boom";
import fs from "node:fs";
import pino from "pino";
import qrcode from "qrcode-terminal";
import { config } from "./config.js";
import { logger } from "./logger.js";
import { mapBaileysMessage } from "./messageMap.js";
import { cacheMediaStub } from "./mediaCache.js";
import { messageObservabilityFields } from "./observability.js";
import { forwardEventToAspNet } from "./webhook.js";

/** @type {ReturnType<typeof makeWASocket> | null} */
let sock = null;
let status = "DISCONNECTED";
let reconnectTimer = null;
let starting = false;
/** @type {Set<string>} */
const botSentIds = new Set();

export function getStatus() {
  return status;
}

export function getSocket() {
  return sock;
}

export function markBotSent(externalMessageId) {
  if (!externalMessageId) return;
  botSentIds.add(externalMessageId);
  if (botSentIds.size > 5000) {
    const first = botSentIds.values().next().value;
    botSentIds.delete(first);
  }
}

function setStatus(next) {
  if (status === next) return;
  status = next;
  logger.info({ status }, "connection status changed");
  void forwardEventToAspNet({
    eventType: "ConnectionStateChanged",
    status,
    timestamp: new Date().toISOString(),
  });
}

function ensureSessionDir() {
  fs.mkdirSync(config.sessionDir, { recursive: true, mode: 0o700 });
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  const delayMs = 5_000;
  logger.warn({ delayMs }, "disconnected; scheduling reconnect");
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    void startWhatsApp().catch((err) => {
      logger.error({ err: err?.message }, "reconnect failed");
      setStatus("ERROR");
    });
  }, delayMs);
}

export async function startWhatsApp() {
  if (starting) return;
  starting = true;
  try {
    ensureSessionDir();
    setStatus("CONNECTING");

    const { state, saveCreds } = await useMultiFileAuthState(config.sessionDir);
    const { version } = await fetchLatestBaileysVersion();

    // Keep Baileys protocol logs quiet — they can include sensitive handshake fields.
    const baileysLogger = pino({ level: "silent" });

    sock = makeWASocket({
      version,
      auth: state,
      logger: baileysLogger,
      printQRInTerminal: false,
      syncFullHistory: false,
      markOnlineOnConnect: false,
    });

    sock.ev.on("creds.update", saveCreds);

    sock.ev.on("connection.update", (update) => {
      const { connection, lastDisconnect, qr } = update;

      if (qr) {
        setStatus("AUTHENTICATION_REQUIRED");
        if (process.stdout.isTTY) {
          console.log("\n=== WhatsApp QR (scan with Linked devices). Do not paste into chats/logs. ===\n");
          qrcode.generate(qr, { small: true });
          console.log("\n=== End QR ===\n");
        } else {
          logger.warn("AUTHENTICATION_REQUIRED — run connector in an interactive terminal to display QR");
        }
      }

      if (connection === "open") {
        setStatus("CONNECTED");
      } else if (connection === "connecting") {
        setStatus("CONNECTING");
      } else if (connection === "close") {
        const statusCode = new Boom(lastDisconnect?.error)?.output?.statusCode;
        const loggedOut = statusCode === DisconnectReason.loggedOut;
        setStatus(loggedOut ? "AUTHENTICATION_REQUIRED" : "DISCONNECTED");

        if (!loggedOut) {
          scheduleReconnect();
        } else {
          logger.warn("session logged out; QR required again");
        }
      }
    });

    sock.ev.on("messages.upsert", async ({ messages, type }) => {
      if (type !== "notify" && type !== "append") {
        logger.info(
          { event: "BAILEYS_UPSERT_FILTERED", upsertType: type, batchSize: messages?.length ?? 0 },
          "messages.upsert batch ignored by upsert type filter",
        );
        return;
      }

      for (const msg of messages) {
        try {
          if (!msg?.key?.id) {
            logger.debug({ event: "BAILEYS_MESSAGE_SKIPPED", reason: "missing_message_id" }, "message skipped");
            continue;
          }
          if (msg.key.remoteJid === "status@broadcast") {
            logger.debug({ event: "BAILEYS_MESSAGE_SKIPPED", reason: "status_broadcast" }, "message skipped");
            continue;
          }

          const mapped = mapBaileysMessage(msg);
          const botOriginated = botSentIds.has(mapped.externalMessageId);

          cacheMediaStub(msg);

          logger.info(
            {
              event: "BAILEYS_MESSAGE_RECEIVED",
              upsertType: type,
              ...messageObservabilityFields(mapped, { botOriginated }),
            },
            "messages.upsert event received",
          );

          logger.info(
            {
              event: "WEBHOOK_FORWARD_STARTED",
              eventType: mapped.eventType,
              ...messageObservabilityFields(mapped, { botOriginated }),
            },
            "webhook forwarding started",
          );

          await forwardEventToAspNet({
            ...mapped,
            botOriginated,
            upsertType: type,
          });
        } catch (error) {
          logger.error({ err: error?.message, event: "BAILEYS_MESSAGE_HANDLER_ERROR" }, "failed to handle inbound message");
        }
      }
    });
  } finally {
    starting = false;
  }
}

export async function sendText(toPhoneDigits, text) {
  if (!sock || status !== "CONNECTED") {
    throw new Error(`Connector not connected (status=${status})`);
  }

  const jid = `${toPhoneDigits.replace(/\D/g, "")}@s.whatsapp.net`;
  const result = await sock.sendMessage(jid, { text });
  const id = result?.key?.id;
  if (id) {
    markBotSent(id);
  }
  return { externalMessageId: id || null, remoteJid: jid };
}

/**
 * Download inbound media by WhatsApp external message id.
 * Requires the message stub to still be cached locally.
 */
export async function downloadMediaByMessageId(externalMessageId) {
  if (!sock || status !== "CONNECTED") {
    throw new Error(`Connector not connected (status=${status})`);
  }

  const { getCachedStub, extractMimeType } = await import("./mediaCache.js");
  const stub = getCachedStub(externalMessageId);
  if (!stub) {
    const err = new Error("media_stub_not_found");
    err.code = "MEDIA_STUB_NOT_FOUND";
    throw err;
  }

  const buffer = await downloadMediaMessage(
    stub,
    "buffer",
    {},
    { logger: pino({ level: "silent" }), reuploadRequest: sock.updateMediaMessage },
  );

  return {
    mimeType: extractMimeType(stub.message),
    content: buffer,
    byteLength: buffer?.length || 0,
  };
}
