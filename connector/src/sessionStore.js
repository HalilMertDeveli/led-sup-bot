import fs from "node:fs";
import path from "node:path";

export function resolveSessionDirectory(connectorRoot, configuredPath = ".sessions") {
  const root = path.resolve(connectorRoot);
  const value = String(configuredPath || ".sessions").trim() || ".sessions";
  return path.resolve(root, value);
}

export function ensureSessionDirectory(sessionDir) {
  assertSafeSessionDirectory(sessionDir);
  fs.mkdirSync(sessionDir, { recursive: true, mode: 0o700 });
}

/**
 * Atomically removes the active auth state from use while retaining a local,
 * ignored backup for recovery. The replacement directory is created at the
 * exact same absolute path consumed by useMultiFileAuthState.
 */
export function archiveAndResetSessionDirectory(sessionDir, now = new Date()) {
  const absolute = assertSafeSessionDirectory(sessionDir);
  const parent = path.dirname(absolute);
  const backupRoot = path.join(parent, ".session-backups");
  let backupPath = null;

  if (fs.existsSync(absolute)) {
    const info = fs.lstatSync(absolute);
    if (info.isSymbolicLink()) {
      throw new Error("Refusing to reset a symlinked session directory");
    }
    if (!info.isDirectory()) {
      throw new Error("Configured session path is not a directory");
    }

    fs.mkdirSync(backupRoot, { recursive: true, mode: 0o700 });
    const stamp = now.toISOString().replace(/[:.]/g, "-");
    backupPath = uniqueBackupPath(backupRoot, `session-${stamp}`);
    fs.renameSync(absolute, backupPath);
  }

  fs.mkdirSync(absolute, { recursive: true, mode: 0o700 });
  return { sessionDir: absolute, backupCreated: Boolean(backupPath), backupPath };
}

function assertSafeSessionDirectory(sessionDir) {
  const absolute = path.resolve(String(sessionDir || ""));
  const root = path.parse(absolute).root;
  if (!path.isAbsolute(absolute) || absolute === root) {
    throw new Error("Unsafe WhatsApp session directory");
  }
  return absolute;
}

function uniqueBackupPath(backupRoot, baseName) {
  let candidate = path.join(backupRoot, baseName);
  for (let suffix = 1; fs.existsSync(candidate); suffix += 1) {
    candidate = path.join(backupRoot, `${baseName}-${suffix}`);
  }
  return candidate;
}
