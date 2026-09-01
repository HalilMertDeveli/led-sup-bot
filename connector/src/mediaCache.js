/**
 * In-memory cache of recent Baileys message stubs for controlled media download.
 * Stubs are never forwarded to ASP.NET — only metadata is sent via webhook.
 */

const MAX_ENTRIES = 2000;
const TTL_MS = 60 * 60 * 1000;

/** @type {Map<string, { stub: object, cachedAt: number }>} */
const cache = new Map();

function hasDownloadableMedia(message) {
  const image = message?.imageMessage;
  const document = message?.documentMessage;
  const video = message?.videoMessage;
  const audio = message?.audioMessage;
  const media = image || document || video || audio;
  if (!media) return false;
  return Boolean(media.mediaKey || media.directPath || media.url);
}

export function cacheMediaStub(msg) {
  const id = msg?.key?.id;
  if (!id || !msg?.message || !hasDownloadableMedia(msg.message)) {
    return;
  }

  cache.set(id, { stub: msg, cachedAt: Date.now() });
  prune();
}

export function getCachedStub(externalMessageId) {
  const entry = cache.get(externalMessageId);
  if (!entry) return null;
  if (Date.now() - entry.cachedAt > TTL_MS) {
    cache.delete(externalMessageId);
    return null;
  }
  return entry.stub;
}

function prune() {
  const now = Date.now();
  for (const [id, entry] of cache.entries()) {
    if (now - entry.cachedAt > TTL_MS) {
      cache.delete(id);
    }
  }

  while (cache.size > MAX_ENTRIES) {
    const oldest = cache.keys().next().value;
    cache.delete(oldest);
  }
}

export function extractMimeType(message) {
  const image = message?.imageMessage;
  const document = message?.documentMessage;
  const video = message?.videoMessage;
  const audio = message?.audioMessage;
  const media = image || document || video || audio;
  return media?.mimetype || "application/octet-stream";
}
