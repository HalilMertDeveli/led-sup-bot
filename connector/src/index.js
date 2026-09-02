import crypto from "node:crypto";
import express from "express";
import { config } from "./config.js";
import { logger } from "./logger.js";
import { getStatus, sendText, sendPresence, startWhatsApp, downloadMediaByMessageId } from "./whatsapp.js";
import { getConnectedSelfJids } from "./selfIdentity.js";
import { getSignalDiagnosticCounters } from "./safeSignalDiagnostics.js";

const app = express();
app.use(express.json({ limit: "2mb" }));

function requireSecret(req, res, next) {
  const provided = req.header("X-Connector-Secret") || "";
  const expected = config.sharedSecret || "";
  const a = Buffer.from(provided);
  const b = Buffer.from(expected);
  if (!expected || a.length !== b.length || !crypto.timingSafeEqual(a, b)) {
    return res.status(401).json({ error: "unauthorized" });
  }
  return next();
}

app.get("/status", requireSecret, (_req, res) => {
  res.json({
    status: getStatus(),
    unofficial: true,
    metaApproved: false,
    selfIdentityRegistered: getConnectedSelfJids().size > 0,
    signalDiagnostics: getSignalDiagnosticCounters(),
  });
});

app.get("/self-jids", requireSecret, (_req, res) => {
  res.json({ jids: [...getConnectedSelfJids()] });
});

app.post("/send/text", requireSecret, async (req, res) => {
  try {
    const toRaw = String(req.body?.to || "").trim();
    const text = String(req.body?.text || "");
    if (!toRaw || !text) {
      return res.status(400).json({ error: "to and text are required" });
    }
    const result = await sendText(toRaw, text);
    return res.json({
      ok: true,
      externalMessageId: result.externalMessageId,
      destinationType: result.destinationType,
      baileysMessageIdPresent: Boolean(result.externalMessageId),
      exactRoutePreserved: result.exactRoutePreserved ?? true,
      sockSendMessageCalled: true,
    });
  } catch (error) {
    logger.error({ err: error?.message }, "send text failed");
    return res.status(503).json({ error: "send_failed" });
  }
});

app.post("/send/presence", requireSecret, async (req, res) => {
  try {
    const toRaw = String(req.body?.to || "").trim();
    const state = String(req.body?.state || "").trim().toLowerCase();
    if (!toRaw || (state !== "composing" && state !== "paused")) {
      return res.status(400).json({ error: "to and state (composing|paused) are required" });
    }
    const result = await sendPresence(toRaw, state);
    return res.json({ ok: true, remoteJid: result.remoteJid, state: result.state });
  } catch (error) {
    logger.warn({ err: error?.message }, "send presence failed");
    return res.status(503).json({ error: "presence_failed" });
  }
});

app.post("/download/media", requireSecret, async (req, res) => {
  try {
    const externalMessageId = String(req.body?.externalMessageId || "").trim();
    if (!externalMessageId) {
      return res.status(400).json({ error: "external_message_id_required" });
    }

    const result = await downloadMediaByMessageId(externalMessageId);
    if (!result?.content?.length) {
      return res.status(404).json({ error: "media_not_available" });
    }

    res.setHeader("Content-Type", result.mimeType || "application/octet-stream");
    res.setHeader("X-Media-Byte-Length", String(result.byteLength));
    return res.send(result.content);
  } catch (error) {
    if (error?.code === "MEDIA_STUB_NOT_FOUND") {
      return res.status(404).json({ error: "media_stub_not_found" });
    }
    logger.error({ err: error?.message, event: "MEDIA_DOWNLOAD_FAILED" }, "media download failed");
    return res.status(503).json({ error: "download_failed" });
  }
});

app.listen(config.port, "127.0.0.1", () => {
  logger.info({ port: config.port }, "connector HTTP listening on loopback only");
  startWhatsApp().catch((error) => {
    logger.error({ err: error?.message }, "failed to start WhatsApp session");
    // Do not crash the HTTP process; status endpoints remain available.
  });
});
