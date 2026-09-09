import test from "node:test";
import assert from "node:assert/strict";
import { createQrMatrix } from "../src/qrMatrix.js";

test("pairing payload becomes a square boolean QR matrix", () => {
  const matrix = createQrMatrix("test-only-pairing-payload");
  assert.ok(matrix.length >= 21);
  assert.equal(matrix.every((row) => row.length === matrix.length), true);
  assert.equal(matrix.flat().every((value) => typeof value === "boolean"), true);
});
