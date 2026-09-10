import test from "node:test";
import assert from "node:assert/strict";
import { assertNonSelfJid, isSelfJid, setConnectedSelfJids, remoteJidType } from "../src/selfIdentity.js";

test("isSelfJid detects connected business lid", () => {
  setConnectedSelfJids(["905469124845@s.whatsapp.net", "268040956571702:92@lid"]);
  assert.equal(isSelfJid("268040956571702@lid"), true);
  assert.equal(isSelfJid("268040956571702:92@lid"), true);
  assert.equal(isSelfJid("207855059775610@lid"), false);
});

test("remoteJidType classifies lid and pn", () => {
  assert.equal(remoteJidType("123@lid"), "lid");
  assert.equal(remoteJidType("905551234567@s.whatsapp.net"), "pn");
});

test("outbound guard blocks both authenticated PN and LID identities", () => {
  setConnectedSelfJids(["905551111111:7@s.whatsapp.net", "111222333:4@lid"]);

  assert.throws(() => assertNonSelfJid("905551111111@s.whatsapp.net"), { code: "SELF_DESTINATION" });
  assert.throws(() => assertNonSelfJid("111222333@lid"), { code: "SELF_DESTINATION" });
  assert.doesNotThrow(() => assertNonSelfJid("905552222222@s.whatsapp.net"));
});
