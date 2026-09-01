import { useCallback, useEffect, useState } from "react";
import {
  dismissAnyDeskAlert,
  fetchCompactFocus,
  logout,
  pause,
  resumeBot,
  takeover,
} from "../api/admin";
import type { CompactFocus } from "../types";
import { normalizeBotMode } from "../utils/messagePresentation";

interface CompactPanelProps {
  onLogout: () => void;
  onOpenFull: () => void;
}

export function CompactPanel({ onLogout, onOpenFull }: CompactPanelProps) {
  const [focus, setFocus] = useState<CompactFocus | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const data = await fetchCompactFocus();
      setFocus(data);
      setError(null);
    } catch {
      setError("Panel verisi yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    const timer = setInterval(() => void refresh(), 3000);
    return () => clearInterval(timer);
  }, [refresh]);

  async function runAction(kind: "pause" | "resume" | "takeover" | "dismiss") {
    if (!focus?.conversationId) return;
    setActionLoading(kind);
    setError(null);
    try {
      if (kind === "pause") await pause(focus.conversationId);
      else if (kind === "resume") await resumeBot(focus.conversationId);
      else if (kind === "takeover") await takeover(focus.conversationId);
      else await dismissAnyDeskAlert(focus.conversationId);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "İşlem başarısız.");
    } finally {
      setActionLoading(null);
    }
  }

  const botMode = focus?.botMode != null ? normalizeBotMode(focus.botMode) : null;
  const botActive = botMode === "Bot";

  return (
    <div className="compact">
      <header className="compact__header">
        <div>
          <p className="compact__eyebrow">LED Support — Compact</p>
          <strong>{focus?.customerDisplayName ?? "Konuşma yok"}</strong>
        </div>
        <div className="compact__header-actions">
          <button type="button" className="compact__link" onClick={onOpenFull}>
            Tam panel
          </button>
          <button type="button" className="compact__link" onClick={() => void logout().then(onLogout)}>
            Çıkış
          </button>
        </div>
      </header>

      {loading && <p className="compact__state">Yükleniyor…</p>}
      {error && (
        <p className="compact__error" role="alert">
          {error}
        </p>
      )}

      {focus && (
        <>
          <section className="compact__bot">
            <p className="compact__label">BOT</p>
            <p className="compact__status-line">
              {botActive ? "● Aktif" : "○ Durduruldu"}
              {focus.botReplyStatusLabel ? ` — ${focus.botReplyStatusLabel}` : ""}
            </p>
            <div className="compact__controls">
              {botActive ? (
                <button type="button" disabled={!!actionLoading} onClick={() => void runAction("pause")}>
                  {actionLoading === "pause" ? "…" : "DURDUR"}
                </button>
              ) : (
                <button type="button" disabled={!!actionLoading} onClick={() => void runAction("resume")}>
                  {actionLoading === "resume" ? "…" : "ÇALIŞTIR"}
                </button>
              )}
            </div>
          </section>

          {focus.showAnyDeskAlert && (
            <section className="compact__alert" role="alert">
              <strong>AnyDesk aşamasına geldi</strong>
              <p>{focus.issueSummary ?? "Sorun özeti henüz yok"}</p>
              <ul>
                <li>Ekran: {focus.screenSummary ?? "—"}</li>
                <li>Receiver: {focus.receiverSummary ?? "—"}</li>
                <li>Panel: {focus.panelStatus}</li>
                <li>Config: {focus.configStatus}</li>
                <li>AnyDesk: {focus.anyDeskStatus}</li>
              </ul>
              <div className="compact__alert-actions">
                <button type="button" disabled={!!actionLoading} onClick={() => void runAction("takeover")}>
                  DEVRAL
                </button>
                <button type="button" disabled={!!actionLoading} onClick={() => void runAction("dismiss")}>
                  BOT DEVAM ETSİN
                </button>
              </div>
            </section>
          )}

          <section className="compact__facts">
            <Row label="ISSUE" value={focus.issueSummary ?? "—"} />
            <Row label="SCREEN" value={focus.screenSummary ?? "—"} />
            <Row label="RECEIVER" value={focus.receiverSummary ?? "—"} />
            <Row label="PANEL" value={focus.panelStatus} />
            <Row label="CONFIG" value={focus.configStatus} />
            <Row label="ANYDESK" value={focus.anyDeskStatus} />
          </section>

          <section className="compact__recent">
            <p className="compact__label">RECENT MESSAGES</p>
            <ul>
              {focus.recentMessages.length === 0 && <li className="compact__state">Mesaj yok</li>}
              {focus.recentMessages.map((m, i) => (
                <li key={`${m.createdAt}-${i}`}>
                  <span className="compact__msg-role">{m.role}</span> {m.content}
                </li>
              ))}
            </ul>
          </section>
        </>
      )}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="compact__row">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
