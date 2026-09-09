import test from "node:test";
import assert from "node:assert/strict";
import { getSignalDiagnosticCounters, installSafeSignalDiagnostics, resetSignalDiagnosticCounters, shouldMarkSessionDegraded } from "../src/safeSignalDiagnostics.js";

test("signal diagnostics installation is idempotent and exposes counters only", () => {
  installSafeSignalDiagnostics();
  installSafeSignalDiagnostics();

  assert.deepEqual(Object.keys(getSignalDiagnosticCounters()).sort(), [
    "badMac",
    "closedSessionUsed",
    "decryptFailed",
    "incomingPreKeyRotation",
  ]);
});

test("persistent signal decryption failures mark the linked session degraded", () => {
  assert.equal(shouldMarkSessionDegraded({ badMac: 9, decryptFailed: 4 }), false);
  assert.equal(shouldMarkSessionDegraded({ badMac: 10, decryptFailed: 0 }), true);
  assert.equal(shouldMarkSessionDegraded({ badMac: 0, decryptFailed: 5 }), true);
});

test("an intentional auth reset clears diagnostics for the new session", () => {
  resetSignalDiagnosticCounters();
  assert.deepEqual(getSignalDiagnosticCounters(), {
    badMac: 0,
    decryptFailed: 0,
    closedSessionUsed: 0,
    incomingPreKeyRotation: 0,
  });
});
