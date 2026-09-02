import test from "node:test";
import assert from "node:assert/strict";
import { jidToPhoneE164, resolveOutboundJid, normalizeWhatsAppJid } from "../src/messageMap.js";

test("jidToPhoneE164 extracts E.164 from s.whatsapp.net", () => {
  assert.equal(jidToPhoneE164("905551234567@s.whatsapp.net"), "905551234567");
});

test("jidToPhoneE164 returns null for lid JIDs", () => {
  assert.equal(jidToPhoneE164("207855059775610@lid"), null);
});

test("mapBaileysMessage uses senderPn when remoteJid is lid", async () => {
  const { mapBaileysMessage } = await import("../src/messageMap.js");
  const mapped = mapBaileysMessage({
    key: {
      remoteJid: "207855059775610@lid",
      senderPn: "905551234567@s.whatsapp.net",
      id: "ABC123",
      fromMe: false,
    },
    message: { conversation: "hello" },
    messageTimestamp: 1_700_000_000,
  });
  assert.equal(mapped.remotePhoneNumber, "905551234567");
  assert.equal(mapped.senderPnJid, "905551234567@s.whatsapp.net");
});

test("normalizeWhatsAppJid strips device suffix from lid", () => {
  assert.equal(normalizeWhatsAppJid("268040956571702:91@lid"), "268040956571702@lid");
});

test("resolveOutboundJid preserves full @lid", () => {
  assert.equal(resolveOutboundJid("207855059775610@lid"), "207855059775610@lid");
});

test("resolveOutboundJid normalizes device suffix on @lid", () => {
  assert.equal(resolveOutboundJid("268040956571702:91@lid"), "268040956571702@lid");
});

test("resolveOutboundJid builds s.whatsapp.net from digits", () => {
  assert.equal(resolveOutboundJid("905551234567"), "905551234567@s.whatsapp.net");
});

test("resolveOutboundJid preserves @s.whatsapp.net", () => {
  assert.equal(resolveOutboundJid("905551234567@s.whatsapp.net"), "905551234567@s.whatsapp.net");
});

test("resolveOutboundJid does not digit-strip full @lid JIDs", () => {
  const input = "268040956571702@lid";
  assert.equal(resolveOutboundJid(input), input);
});
