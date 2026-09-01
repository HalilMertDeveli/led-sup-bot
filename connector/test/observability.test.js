import test from "node:test";
import assert from "node:assert/strict";
import { redactJid, messageObservabilityFields } from "../src/observability.js";

test("redactJid masks E.164 user JIDs", () => {
  const redacted = redactJid("905551234567@s.whatsapp.net");
  assert.ok(redacted.endsWith("@s.whatsapp.net"));
  assert.ok(redacted.endsWith("4567@s.whatsapp.net"));
  assert.ok(!redacted.includes("905551234567"));
});

test("redactJid masks group JIDs without exposing id", () => {
  assert.equal(redactJid("120363012345678901@g.us"), "group:***@g.us");
});

test("redactJid masks lid JIDs", () => {
  assert.equal(redactJid("abc123xyz@lid"), "lid:***@lid");
});

test("redactJid handles device suffix", () => {
  const redacted = redactJid("905551234567:99@s.whatsapp.net");
  assert.ok(redacted.endsWith("@s.whatsapp.net"));
  assert.ok(redacted.includes("4567"));
  assert.ok(!redacted.includes("905551234567"));
});

test("messageObservabilityFields excludes content and phone", () => {
  const fields = messageObservabilityFields(
    {
      externalMessageId: "MSG123",
      fromMe: true,
      remoteJid: "905551234567@s.whatsapp.net",
      messageType: "Text",
      textPresent: true,
      textContent: "SECRET",
      remotePhoneNumber: "905551234567",
    },
    { botOriginated: false },
  );

  assert.equal(fields.messageId, "MSG123");
  assert.equal(fields.fromMe, true);
  assert.equal(fields.messageType, "Text");
  assert.equal(fields.textPresent, true);
  assert.equal(fields.botOriginated, false);
  assert.ok(fields.remoteJid.includes("@s.whatsapp.net"));
  assert.equal(fields.textContent, undefined);
  assert.equal(fields.remotePhoneNumber, undefined);
});
