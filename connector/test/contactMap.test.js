import test from "node:test";
import assert from "node:assert/strict";
import { mapContactUpdate } from "../src/contactMap.js";

test("saved contact name and trusted LID/PN mapping are preserved", () => {
  assert.deepEqual(mapContactUpdate({
    id: "123@lid", lid: "123@lid", jid: "905551234567@s.whatsapp.net", name: " Ahmet LED ", notify: "Public Name",
  }), {
    eventType: "ContactUpdated", remoteJid: "123@lid", lidJid: "123@lid",
    phoneJid: "905551234567@s.whatsapp.net", savedDisplayName: "Ahmet LED",
  });
});

test("public push name is not treated as operator saved contact name", () => {
  const mapped = mapContactUpdate({ id: "905551234567@s.whatsapp.net", notify: "Public Name" });
  assert.equal(mapped.savedDisplayName, null);
  assert.equal(mapped.phoneJid, "905551234567@s.whatsapp.net");
});

test("unknown identifiers do not fabricate a phone mapping", () => {
  assert.equal(mapContactUpdate({ id: "opaque" }), null);
});
