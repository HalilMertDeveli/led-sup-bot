import crypto from "node:crypto";
import express from "express";
import { config } from "./config.js";
import { logger } from "./logger.js";
import { getStatus, sendText, sendImage, sendDocument, sendAudio, sendPresence, startWhatsApp, downloadMediaByMessageId } from "./whatsapp.js";
import { getConnectedSelfJids } from "./selfIdentity.js";
import { getSignalDiagnosticCounters } from "./safeSignalDiagnostics.js";
import { documentJsonLimit, validateOutboundDocument } from "./documentPolicy.js";

const app = express();
const standardJson = express.json({ limit: "2mb" });
app.use((req, res, next) => req.path === "/send/document" || req.path === "/send/audio" ? next() : standardJson(req, res, next));

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

app.post("/send/image", requireSecret, async (req, res) => {
  try {
    const toRaw = String(req.body?.to || "").trim();
    const caption = req.body?.caption ? String(req.body.caption) : null;
    const imageBase64 = String(req.body?.imageBase64 || "").trim();

    if (!toRaw || !imageBase64) {
      return res.status(400).json({ error: "to and imageBase64 are required" });
    }

    const imageBuffer = Buffer.from(imageBase64, "base64");
    if (imageBuffer.length === 0) {
      return res.status(400).json({ error: "invalid_image_data" });
    }

    const result = await sendImage(toRaw, imageBuffer, caption);
    return res.json({
      ok: true,
      externalMessageId: result.externalMessageId,
      destinationType: result.destinationType,
      baileysMessageIdPresent: Boolean(result.externalMessageId),
      exactRoutePreserved: result.exactRoutePreserved ?? true,
      sockSendMessageCalled: true,
    });
  } catch (error) {
    logger.error({ err: error?.message }, "send image failed");
    return res.status(503).json({ error: "send_image_failed" });
  }
});

app.post("/send/document", requireSecret, express.json({ limit: documentJsonLimit }), async (req, res) => {
  try {
    const toRaw = String(req.body?.to || "").trim();
    const documentBase64 = String(req.body?.documentBase64 || "").trim();
    const fileName = String(req.body?.fileName || "").trim();
    const contentType = String(req.body?.contentType || "application/octet-stream").trim();
    const caption = req.body?.caption ? String(req.body.caption) : null;
    if (!toRaw || !documentBase64)
      return res.status(400).json({ error: "invalid_document_request" });
    const documentBuffer = Buffer.from(documentBase64, "base64");
    const policyError = validateOutboundDocument(fileName, documentBuffer);
    if (policyError) return res.status(policyError === "document_too_large" ? 413 : 400).json({ error: policyError });
    const result = await sendDocument(toRaw, documentBuffer, contentType, fileName, caption);
    return res.json({ ok: true, externalMessageId: result.externalMessageId,
      destinationType: result.destinationType, baileysMessageIdPresent: Boolean(result.externalMessageId),
      exactRoutePreserved: result.exactRoutePreserved ?? true, sockSendMessageCalled: true });
  } catch (error) {
    logger.error({ err: error?.message }, "send document failed");
    return res.status(503).json({ error: "send_document_failed" });
  }
});

app.post("/send/audio", requireSecret, express.json({ limit: "16mb" }), async (req, res) => {
  try {
    const toRaw = String(req.body?.to || "").trim();
    const audioBase64 = String(req.body?.audioBase64 || "").trim();
    const contentType = String(req.body?.contentType || "audio/ogg; codecs=opus").trim();
    if (!toRaw || !audioBase64) return res.status(400).json({ error: "invalid_audio_request" });
    const audioBuffer = Buffer.from(audioBase64, "base64");
    if (!audioBuffer.length || audioBuffer.length > 10 * 1024 * 1024) return res.status(413).json({ error: "audio_too_large" });
    const result = await sendAudio(toRaw, audioBuffer, contentType, req.body?.voiceNote !== false);
    return res.json({ ok: true, externalMessageId: result.externalMessageId,
      destinationType: result.destinationType, exactRoutePreserved: result.exactRoutePreserved ?? true });
  } catch (error) {
    logger.error({ err: error?.message }, "send audio failed");
    return res.status(503).json({ error: "send_audio_failed" });
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
