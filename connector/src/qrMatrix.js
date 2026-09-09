import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const QRCode = require("qrcode-terminal/vendor/QRCode");
const QRErrorCorrectLevel = require("qrcode-terminal/vendor/QRCode/QRErrorCorrectLevel");

/** Returns only the QR module matrix; the sensitive pairing payload is never exposed or logged. */
export function createQrMatrix(pairingPayload) {
  if (!pairingPayload) return [];
  const qr = new QRCode(-1, QRErrorCorrectLevel.L);
  qr.addData(pairingPayload);
  qr.make();
  return qr.modules.map((row) => row.map((cell) => Boolean(cell)));
}
