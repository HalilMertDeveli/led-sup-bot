import test from "node:test";
import assert from "node:assert/strict";
import { documentJsonLimit, maxDocumentBytes, validateOutboundDocument } from "../src/documentPolicy.js";

test("document JSON limit covers maximum firmware after base64 expansion", () => {
  const maximum = maxDocumentBytes("firmware.hex");
  const encodedBytes = Math.ceil(maximum / 3) * 4;
  assert.ok(Number.parseInt(documentJsonLimit, 10) * 1024 * 1024 > encodedBytes);
});

test("document policy accepts exact bounds and rejects excess or unsafe names", () => {
  assert.equal(validateOutboundDocument("firmware.hex", Buffer.alloc(16 * 1024 * 1024)), null);
  assert.equal(validateOutboundDocument("receiver.rcvbp", Buffer.alloc(8 * 1024 * 1024)), null);
  assert.equal(validateOutboundDocument("firmware.hex", Buffer.alloc(16 * 1024 * 1024 + 1)), "document_too_large");
  assert.equal(validateOutboundDocument("payload.exe.hex", Buffer.from([1])), "invalid_document_name");
  assert.equal(validateOutboundDocument("../firmware.hex", Buffer.from([1])), "invalid_document_name");
  assert.equal(validateOutboundDocument("firmware.bin", Buffer.from([1])), "invalid_document_type");
});
