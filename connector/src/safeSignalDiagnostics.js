/**
 * libsignal writes session objects directly to console on some error paths.
 * Keep diagnostics useful without allowing ratchet/key material into logs.
 */
const counters = {
  badMac: 0,
  decryptFailed: 0,
  closedSessionUsed: 0,
  incomingPreKeyRotation: 0,
};

let installed = false;

export function installSafeSignalDiagnostics() {
  if (installed) return;
  installed = true;

  const originalError = console.error.bind(console);
  const originalWarn = console.warn.bind(console);

  console.error = (...args) => {
    const text = args.map(toSafeText).join(" ");
    if (text.includes("Bad MAC")) {
      counters.badMac += 1;
      originalError(`[signal] decrypt_failed class=BadMAC count=${counters.badMac}`);
      return;
    }
    if (text.includes("Failed to decrypt message with any known session")) {
      counters.decryptFailed += 1;
      originalError(`[signal] decrypt_failed class=NoMatchingSession count=${counters.decryptFailed}`);
      return;
    }
    originalError(...args.map(redactNonPrimitive));
  };

  console.warn = (...args) => {
    const text = args.map(toSafeText).join(" ");
    if (text.includes("Session already closed")) {
      originalWarn("[signal] session_state=already_closed details=redacted");
      return;
    }
    if (text.includes("Decrypted message with closed session")) {
      counters.closedSessionUsed += 1;
      originalWarn(`[signal] decrypt_recovered source=closed_session count=${counters.closedSessionUsed}`);
      return;
    }
    if (text.includes("Closing open session in favor of incoming prekey bundle")) {
      counters.incomingPreKeyRotation += 1;
      originalWarn(`[signal] session_rotated reason=incoming_prekey count=${counters.incomingPreKeyRotation}`);
      return;
    }
    originalWarn(...args.map(redactNonPrimitive));
  };
}

export function getSignalDiagnosticCounters() {
  return { ...counters };
}

function toSafeText(value) {
  return value instanceof Error ? `${value.name}: ${value.message}` : String(value);
}

function redactNonPrimitive(value) {
  if (value === null || ["string", "number", "boolean", "undefined"].includes(typeof value)) {
    return value;
  }
  return "[object redacted]";
}

installSafeSignalDiagnostics();
