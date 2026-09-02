import test from "node:test";
import assert from "node:assert/strict";
import { getSignalDiagnosticCounters, installSafeSignalDiagnostics } from "../src/safeSignalDiagnostics.js";

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
