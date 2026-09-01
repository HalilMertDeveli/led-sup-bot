import crypto from "node:crypto";
import express from "express";
import { config } from "./config.js";
import { logger } from "./logger.js";
import { getStatus, sendText, startWhatsApp } from "./whatsapp.js";

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
  });
});

app.post("/send/text", requireSecret, async (req, res) => {
  try {
    const to = String(req.body?.to || "").replace(/\D/g, "");
    const text = String(req.body?.text || "");
    if (!to || !text) {
      return res.status(400).json({ error: "to and text are required" });
    }
    const result = await sendText(to, text);
    return res.json({ ok: true, externalMessageId: result.externalMessageId });
  } catch (error) {
    logger.error({ err: error?.message }, "send text failed");
    return res.status(503).json({ error: "send_failed" });
  }
});

app.listen(config.port, "127.0.0.1", () => {
  logger.info({ port: config.port }, "connector HTTP listening on loopback only");
  startWhatsApp().catch((error) => {
    logger.error({ err: error?.message }, "failed to start WhatsApp session");
    // Do not crash the HTTP process; status endpoints remain available.
  });
});
