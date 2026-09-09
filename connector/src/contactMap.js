export function mapContactUpdate(contact) {
  const lidJid = contact?.lid || (contact?.id?.endsWith("@lid") ? contact.id : null);
  const phoneJid = contact?.jid || (contact?.id?.endsWith("@s.whatsapp.net") ? contact.id : null);
  const savedDisplayName = typeof contact?.name === "string" ? contact.name.trim() : null;
  if (!lidJid && !phoneJid) return null;
  return {
    eventType: "ContactUpdated",
    remoteJid: contact.id || lidJid || phoneJid,
    lidJid,
    phoneJid,
    savedDisplayName: savedDisplayName || null,
  };
}
