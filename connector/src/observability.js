/**
 * Privacy-safe log helpers for live pipeline debugging.
 * Never emit message bodies, secrets, or full phone numbers.
 */

/**
 * Redact a WhatsApp JID for logs.
 * Examples:
 *   905551234567@s.whatsapp.net -> ***4567@s.whatsapp.net
 *   120363...@g.us -> group:***@g.us
 *   xxxx@lid -> lid:***@lid
 */
export function redactJid(jid) {
  if (!jid || typeof jid !== "string") {
    return null;
  }

  const at = jid.indexOf("@");
  if (at <= 0) {
    return "redacted";
  }

  const userPart = jid.slice(0, at);
  const domain = jid.slice(at + 1);

  if (domain === "g.us") {
    return `group:***@${domain}`;
  }

  if (domain === "broadcast") {
    return `broadcast@${domain}`;
  }

  if (domain === "lid") {
    return `lid:***@${domain}`;
  }

  const user = userPart.split(":")[0] || userPart;
  if (/^\d+$/.test(user)) {
    return `${maskTrailingDigits(user)}@${domain}`;
  }

  return `***@${domain}`;
}

function maskTrailingDigits(value) {
  if (!value) return "***";
  if (value.length <= 4) {
    return "*".repeat(value.length);
  }
  return "*".repeat(value.length - 4) + value.slice(-4);
}

/** Fields safe to attach when logging webhook/message pipeline metadata. */
export function messageObservabilityFields(mapped, extras = {}) {
  return {
    messageId: mapped?.externalMessageId ?? null,
    fromMe: Boolean(mapped?.fromMe),
    remoteJid: redactJid(mapped?.remoteJid),
    messageType: mapped?.messageType ?? null,
    textPresent: Boolean(mapped?.textPresent),
    ...extras,
  };
}
