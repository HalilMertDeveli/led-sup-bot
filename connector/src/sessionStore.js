import fs from "node:fs";
import path from "node:path";

export function resolveSessionDirectory(connectorRoot, configuredPath = ".sessions") {
  const root = path.resolve(connectorRoot);
  const value = String(configuredPath || ".sessions").trim() || ".sessions";
  return path.resolve(root, value);
}

export function ensureSessionDirectory(sessionDir) {
  const absolute = assertSafeSessionDirectory(sessionDir);
  fs.mkdirSync(absolute, { recursive: true, mode: 0o700 });
  return absolute;
}

/**
 * Prevents two connector processes from mutating the same Baileys auth state.
 * A lock left behind by a dead process is replaced deterministically.
 */
export function acquireSessionProcessLock(sessionDir, pid = process.pid) {
  const absolute = assertSafeSessionDirectory(sessionDir);
  const lockPath = `${absolute}.lock`;
  const payload = `${pid}\n`;

  try {
    const fd = fs.openSync(lockPath, "wx", 0o600);
    fs.writeFileSync(fd, payload, "utf8");
    fs.closeSync(fd);
  } catch (error) {
    if (error?.code !== "EEXIST") throw error;
    const owner = Number.parseInt(fs.readFileSync(lockPath, "utf8").trim(), 10);
    if (Number.isInteger(owner) && isProcessAlive(owner)) {
      throw new Error(`WhatsApp session is already owned by connector pid ${owner}`);
    }
    fs.rmSync(lockPath, { force: true });
    const fd = fs.openSync(lockPath, "wx", 0o600);
    fs.writeFileSync(fd, payload, "utf8");
    fs.closeSync(fd);
  }

  let released = false;
  return {
    lockPath,
    release() {
      if (released) return;
      released = true;
      try {
        const owner = Number.parseInt(fs.readFileSync(lockPath, "utf8").trim(), 10);
        if (owner === pid) fs.rmSync(lockPath, { force: true });
      } catch {
        // The process may be terminating or the lock may already be stale.
      }
    },
  };
}

/** Archives active auth state and recreates the exact path used by Baileys. */
export function archiveAndResetSessionDirectory(sessionDir, now = new Date()) {
  const absolute = assertSafeSessionDirectory(sessionDir);
  const parent = path.dirname(absolute);
  const backupRoot = path.join(parent, ".session-backups");
  let backupPath = null;

  if (fs.existsSync(absolute)) {
    const info = fs.lstatSync(absolute);
    if (info.isSymbolicLink()) throw new Error("Refusing to reset a symlinked session directory");
    if (!info.isDirectory()) throw new Error("Configured session path is not a directory");
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
  if (!path.isAbsolute(absolute) || absolute === path.parse(absolute).root) {
    throw new Error("Unsafe WhatsApp session directory");
  }
  return absolute;
}

function isProcessAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error?.code === "EPERM";
  }
}

function uniqueBackupPath(root, baseName) {
  let candidate = path.join(root, baseName);
  for (let suffix = 1; fs.existsSync(candidate); suffix += 1) {
    candidate = path.join(root, `${baseName}-${suffix}`);
  }
  return candidate;
}
