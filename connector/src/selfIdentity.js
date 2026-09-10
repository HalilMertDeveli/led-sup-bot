/**
 * Connected-account (self) JID helpers. Never log full JID values.
 */

import { normalizeWhatsAppJid, jidDomain } from "./messageMap.js";

/** @type {Set<string>} */
let connectedSelfJids = new Set();

export function setConnectedSelfJids(jids) {
  connectedSelfJids = new Set(
    (jids || [])
      .map((j) => (j ? normalizeWhatsAppJid(j) : null))
      .filter(Boolean),
  );
}

export function getConnectedSelfJids() {
  return connectedSelfJids;
}

export function isSelfJid(jid) {
  if (!jid || connectedSelfJids.size === 0) return false;
  const normalized = normalizeWhatsAppJid(jid);
  return connectedSelfJids.has(normalized);
}

/** Fail closed before any outbound socket operation targets the authenticated account. */
export function assertNonSelfJid(jid) {
  if (isSelfJid(jid)) {
    const error = new Error("Outbound blocked: self destination");
    error.code = "SELF_DESTINATION";
    throw error;
  }
}

export function remoteJidType(jid) {
  const domain = jidDomain(jid);
  if (domain === "lid") return "lid";
  if (domain === "s.whatsapp.net") return "pn";
  if (domain === "g.us") return "group";
  return domain || "unknown";
}
