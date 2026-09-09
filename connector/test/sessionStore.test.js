import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { archiveAndResetSessionDirectory, resolveSessionDirectory } from "../src/sessionStore.js";

test("session path is absolute and independent from process working directory", () => {
  const connectorRoot = path.join(os.tmpdir(), "ledsup-session-root");
  const expected = path.join(connectorRoot, ".sessions");
  assert.equal(resolveSessionDirectory(connectorRoot, ".sessions"), path.resolve(expected));
  assert.equal(path.isAbsolute(resolveSessionDirectory(connectorRoot)), true);
});

test("reset archives and recreates the exact active session directory", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "ledsup-session-reset-"));
  const active = resolveSessionDirectory(root);
  fs.mkdirSync(active);
  fs.writeFileSync(path.join(active, "creds.json"), "test-only");

  try {
    const result = archiveAndResetSessionDirectory(active, new Date("2026-09-08T12:00:00Z"));
    assert.equal(result.sessionDir, active);
    assert.equal(result.backupCreated, true);
    assert.equal(fs.existsSync(path.join(active, "creds.json")), false);
    assert.equal(fs.existsSync(path.join(result.backupPath, "creds.json")), true);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});
