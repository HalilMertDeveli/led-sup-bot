/**
 * Maps Baileys messages to the LedSupBot inbound event contract.
 * Does not log message bodies or phone numbers in full.
 */

export function jidDomain(jid) {
  if (!jid || typeof jid !== "string") return null;
  return jid.split("@")[1]?.split(":")[0] ?? null;
}

export function jidToPhoneE164(jid) {
  if (!jid || typeof jid !== "string") return null;
  const domain = jidDomain(jid);
  // WhatsApp LID user ids are numeric but are NOT dialable phone numbers.
  if (domain === "lid") {
    return null;
  }
  // user@s.whatsapp.net or user:device@s.whatsapp.net
  const user = jid.split("@")[0]?.split(":")[0];
  if (!user || !/^\d+$/.test(user)) return null;
  return user;
}

/** Normalize an outbound destination: E.164 digits or full WhatsApp JID. */
export function resolveOutboundJid(toAddress) {
  const raw = String(toAddress || "").trim();
  if (!raw) return null;
  if (raw.includes("@")) return raw;
  const digits = raw.replace(/\D/g, "");
  if (!digits) return null;
  return `${digits}@s.whatsapp.net`;
}

export function extractText(message) {
  if (!message) return null;
  return (
    message.conversation ||
    message.extendedTextMessage?.text ||
    message.imageMessage?.caption ||
    message.videoMessage?.caption ||
    message.documentMessage?.caption ||
    null
  );
}

export function detectMessageType(message) {
  if (!message) return "Other";
  if (message.conversation || message.extendedTextMessage) return "Text";
  if (message.imageMessage) return "Image";
  if (message.documentMessage) return "Document";
  if (message.audioMessage) return "Audio";
  if (message.videoMessage) return "Video";
  return "Other";
}

export function extractMediaMeta(message) {
  const image = message?.imageMessage;
  const document = message?.documentMessage;
  const video = message?.videoMessage;
  const audio = message?.audioMessage;
  const media = image || document || video || audio;
  if (!media) return null;

  return {
    mimeType: media.mimetype || null,
    fileLength: media.fileLength ? Number(media.fileLength) : null,
    fileName: document?.fileName || null,
    hasMediaKey: Boolean(media.mediaKey),
    // Baileys uses directPath / url internally; we expose presence only, not secrets
    downloadable: Boolean(media.mediaKey || media.directPath || media.url),
  };
}

export function mapBaileysMessage(msg) {
  const remoteJid = msg.key?.remoteJid || null;
  const senderPnJid = msg.key?.senderPn || null;
  const phone = jidToPhoneE164(remoteJid) ?? jidToPhoneE164(senderPnJid);
  const fromMe = Boolean(msg.key?.fromMe);
  const externalMessageId = msg.key?.id || null;
  const messageType = detectMessageType(msg.message);
  const text = extractText(msg.message);
  const media = extractMediaMeta(msg.message);
  const timestamp = msg.messageTimestamp
    ? new Date(Number(msg.messageTimestamp) * 1000).toISOString()
    : new Date().toISOString();

  return {
    eventType: "IncomingMessage",
    externalMessageId,
    remoteJid,
    remotePhoneNumber: phone,
    senderPnJid,
    fromMe,
    messageType,
    text: text ? "[redacted-in-connector-forward]" : null,
    textPresent: Boolean(text),
    // Forward actual text to ASP.NET for persistence (local trusted channel only).
    // ASP.NET must not log it. Connector HTTP is localhost + shared secret.
    textContent: text,
    timestamp,
    media,
  };
}
