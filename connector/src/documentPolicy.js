const MIB = 1024 * 1024;

export const documentJsonLimit = "23mb";

export function maxDocumentBytes(fileName) {
  const lower = String(fileName || "").toLowerCase();
  if (lower.endsWith(".hex")) return 16 * MIB;
  if (lower.endsWith(".rcvbp")) return 8 * MIB;
  return 0;
}

export function validateOutboundDocument(fileName, buffer) {
  const safeName = String(fileName || "").trim();
  if (!safeName || safeName.includes("/") || safeName.includes("\\")) return "invalid_document_name";
  const stem = safeName.slice(0, safeName.lastIndexOf("."));
  if (!stem || stem.includes(".")) return "invalid_document_name";
  const maximum = maxDocumentBytes(safeName);
  if (!maximum) return "invalid_document_type";
  if (!Buffer.isBuffer(buffer) || buffer.length === 0) return "invalid_document_data";
  if (buffer.length > maximum) return "document_too_large";
  return null;
}
