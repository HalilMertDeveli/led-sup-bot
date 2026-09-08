import { onSignalDecryptionFailure, shouldMarkSessionDegraded } from "./safeSignalDiagnostics.js";
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
import { mapBaileysMessage, resolveOutboundJid, isGroupJid, normalizeWhatsAppJid } from "./messageMap.js";
import { cacheMediaStub } from "./mediaCache.js";
import { messageObservabilityFields, redactJid } from "./observability.js";
import { jidDomain } from "./messageMap.js";
import { forwardEventToAspNet } from "./webhook.js";
import { assertNonSelfJid, isSelfJid, remoteJidType, setConnectedSelfJids } from "./selfIdentity.js";
import { mapContactUpdate } from "./contactMap.js";

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

onSignalDecryptionFailure((failureClass, counters) => {
  if (!shouldMarkSessionDegraded(counters) || status === "DEGRADED_SESSION") return;
  logger.error({ event: "WHATSAPP_SESSION_DEGRADED", failureClass, counters },
    "WhatsApp encryption session is unhealthy; linked-device reauthentication required");
  setStatus("DEGRADED_SESSION");
});

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

    sock.ev.on("chats.phoneNumberShare", ({ lid, jid }) => {
      logger.info(
        { event: "LID_PN_MAPPING", lid: redactJid(lid), pnJid: redactJid(jid) },
        "WhatsApp shared phone number for LID contact",
      );
      void forwardEventToAspNet({
        eventType: "LidPhoneMapping",
        remoteJid: lid,
        senderPnJid: jid,
        timestamp: new Date().toISOString(),
      });
    });

    const forwardContacts = (contacts) => {
      for (const contact of contacts ?? []) {
        const mapped = mapContactUpdate(contact);
        if (!mapped) continue;
        void forwardEventToAspNet({
          ...mapped,
          timestamp: new Date().toISOString(),
        });
      }
    };

    sock.ev.on("contacts.upsert", forwardContacts);
    sock.ev.on("contacts.update", forwardContacts);

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
        const selfJids = [sock?.user?.id, sock?.user?.lid].filter(Boolean);
        setConnectedSelfJids(selfJids);
        void forwardEventToAspNet({
          eventType: "ConnectedIdentity",
          selfPnJid: sock?.user?.id ? normalizeWhatsAppJid(sock.user.id) : null,
          selfLidJid: sock?.user?.lid ? normalizeWhatsAppJid(sock.user.lid) : null,
          timestamp: new Date().toISOString(),
        });
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
          if (isGroupJid(msg.key.remoteJid)) {
            logger.info(
              {
                event: "BAILEYS_MESSAGE_SKIPPED",
                reason: "whatsapp_group",
                remoteJid: redactJid(msg.key.remoteJid),
              },
              "group message skipped — no automation or persistence",
            );
            continue;
          }

          const mapped = mapBaileysMessage(msg);
          const botOriginated = botSentIds.has(mapped.externalMessageId);
          const selfMatch = isSelfJid(mapped.remoteJid);

          if (mapped.fromMe) {
            logger.info(
              {
                event: "BAILEYS_MESSAGE_SKIPPED",
                reason: botOriginated ? "bot_outbound_echo" : "from_me",
                fromMe: true,
                remoteJidType: remoteJidType(mapped.remoteJid),
                selfMatch,
                botOriginated,
              },
              "fromMe message skipped — no inbound automation",
            );
            continue;
          }

          logger.info(
            {
              event: "BAILEYS_MESSAGE_RECEIVED",
              upsertType: type,
              fromMe: false,
              remoteJidType: remoteJidType(mapped.remoteJid),
              selfMatch,
              ...messageObservabilityFields(mapped, { botOriginated }),
            },
            "messages.upsert customer inbound",
          );

          cacheMediaStub(msg);

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

    sock.ev.on("messages.update", (updates) => {
      for (const update of updates) {
        const messageId = update?.key?.id;
        if (!messageId || !botSentIds.has(messageId)) {
          continue;
        }

        const deliveryStatus = mapBaileysDeliveryStatus(update?.update?.status);
        if (!deliveryStatus) {
          continue;
        }

        logger.info(
          {
            event: "OUTBOUND_DELIVERY_ACK",
            messageId,
            deliveryStatus,
          },
          "bot outbound delivery acknowledgement",
        );

        void forwardEventToAspNet({
          eventType: "MessageDeliveryUpdate",
          externalMessageId: messageId,
          deliveryStatus,
          timestamp: new Date().toISOString(),
        });
      }
    });
  } finally {
    starting = false;
  }
}

export async function sendText(toAddress, text) {
  if (!sock || status !== "CONNECTED") {
    throw new Error(`Connector not connected (status=${status})`);
  }

  const jid = resolveOutboundJid(toAddress);
  if (!jid) {
    throw new Error("Invalid outbound destination");
  }

  try {
    assertNonSelfJid(jid);
  } catch (error) {
    logger.warn(
      {
        event: "OUTBOUND_SEND_BLOCKED",
        reason: "self_destination",
        destinationType: jidDomain(jid),
      },
      "outbound blocked: self destination",
    );
    throw error;
  }

  const exactRoutePreserved = String(toAddress || "").includes("@")
    ? normalizeWhatsAppJid(toAddress) === jid
    : true;

  const result = await sock.sendMessage(jid, { text });
  const id = result?.key?.id;
  if (id) {
    markBotSent(id);
  }

  logger.info(
    {
      event: "OUTBOUND_SEND_COMPLETED",
      routeSource: "conversation_live_route",
      destinationType: jidDomain(jid),
      exactRoutePreserved,
      connectorRequestAccepted: true,
      sockSendMessageCalled: true,
      baileysMessageIdPresent: Boolean(id),
      messageId: id || null,
      resultRemoteJid: redactJid(result?.key?.remoteJid),
      resultFromMe: Boolean(result?.key?.fromMe),
    },
    "outbound send completed",
  );

  return {
    externalMessageId: id || null,
    remoteJid: jid,
    resultRemoteJid: result?.key?.remoteJid || null,
    destinationType: jidDomain(jid),
    exactRoutePreserved,
  };
}

export async function sendImage(toAddress, imageBuffer, caption) {
  if (!sock || status !== "CONNECTED") {
    throw new Error(`Connector not connected (status=${status})`);
  }

  const jid = resolveOutboundJid(toAddress);
  if (!jid) {
    throw new Error("Invalid outbound destination");
  }

  try {
    assertNonSelfJid(jid);
  } catch (error) {
    logger.warn(
      {
        event: "OUTBOUND_IMAGE_SEND_BLOCKED",
        reason: "self_destination",
        destinationType: jidDomain(jid),
      },
      "outbound blocked: self destination",
    );
    throw error;
  }

  const exactRoutePreserved = String(toAddress || "").includes("@")
    ? normalizeWhatsAppJid(toAddress) === jid
    : true;

  const messagePayload = {
    image: imageBuffer,
    caption: caption || undefined,
  };

  const result = await sock.sendMessage(jid, messagePayload);
  const id = result?.key?.id;
  if (id) {
    markBotSent(id);
  }

  logger.info(
    {
      event: "OUTBOUND_IMAGE_SEND_COMPLETED",
      routeSource: "conversation_live_route",
      destinationType: jidDomain(jid),
      exactRoutePreserved,
      connectorRequestAccepted: true,
      sockSendMessageCalled: true,
      baileysMessageIdPresent: Boolean(id),
      messageId: id || null,
      resultRemoteJid: redactJid(result?.key?.remoteJid),
      resultFromMe: Boolean(result?.key?.fromMe),
    },
    "outbound image send completed",
  );

  return {
    externalMessageId: id || null,
    remoteJid: jid,
    resultRemoteJid: result?.key?.remoteJid || null,
    destinationType: jidDomain(jid),
    exactRoutePreserved,
  };
}

export async function sendDocument(toAddress, documentBuffer, contentType, fileName, caption) {
  if (!sock || status !== "CONNECTED") throw new Error(`Connector not connected (status=${status})`);
  const jid = resolveOutboundJid(toAddress);
  if (!jid) throw new Error("Invalid outbound destination");
  assertNonSelfJid(jid);
  const exactRoutePreserved = String(toAddress || "").includes("@") ? normalizeWhatsAppJid(toAddress) === jid : true;
  const result = await sock.sendMessage(jid, {
    document: documentBuffer,
    mimetype: contentType || "application/octet-stream",
    fileName,
    caption: caption || undefined,
  });
  const id = result?.key?.id;
  if (id) markBotSent(id);
  logger.info({ event: "OUTBOUND_DOCUMENT_SEND_COMPLETED", destinationType: jidDomain(jid),
    exactRoutePreserved, messageId: id || null }, "outbound document send completed");
  return { externalMessageId: id || null, remoteJid: jid, destinationType: jidDomain(jid), exactRoutePreserved };
}

export async function sendAudio(toAddress, audioBuffer, contentType, voiceNote = true) {
  if (!sock) throw new Error("not_connected");
  const remoteJid = resolveOutboundJid(toAddress);
  if (!remoteJid) throw new Error("invalid_destination");
  assertNotSelfDestination(remoteJid);
  const sent = await sock.sendMessage(remoteJid, { audio: audioBuffer, mimetype: contentType, ptt: Boolean(voiceNote) });
  return { externalMessageId: sent?.key?.id || null, destinationType: remoteJidType(remoteJid), exactRoutePreserved: true };
}

function mapBaileysDeliveryStatus(status) {
  if (status === undefined || status === null) return null;
  switch (Number(status)) {
    case 0:
      return "Failed";
    case 1:
    case 2:
      return "Sent";
    case 3:
      return "Delivered";
    case 4:
    case 5:
      return "Read";
    default:
      return null;
  }
}

export async function sendPresence(toAddress, state) {
  if (!sock || status !== "CONNECTED") {
    throw new Error(`Connector not connected (status=${status})`);
  }

  const jid = resolveOutboundJid(toAddress);
  if (!jid) {
    throw new Error("Invalid outbound destination");
  }

  assertNonSelfJid(jid);

  const presence = state === "composing" ? "composing" : "paused";
  await sock.sendPresenceUpdate(presence, jid);
  return { remoteJid: jid, state: presence };
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
