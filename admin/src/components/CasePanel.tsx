import { mediaUrl } from "../api/client";
import type { ConversationDetail } from "../types";
import { botModeClass, botModeLabel } from "../utils/messagePresentation";

interface CasePanelProps {
  detail: ConversationDetail | null;
  loading: boolean;
  error: string | null;
  actionLoading: string | null;
  actionError: string | null;
  onTakeover: () => void;
  onPause: () => void;
  onResume: () => void;
}

export function CasePanel({
  detail,
  loading,
  error,
  actionLoading,
  actionError,
  onTakeover,
  onPause,
  onResume,
}: CasePanelProps) {
  if (loading && !detail) {
    return (
      <aside className="case">
        <p className="case__state">Vaka bilgisi yükleniyor…</p>
      </aside>
    );
  }

  if (error && !detail) {
    return (
      <aside className="case">
        <p className="case__error" role="alert">
          {error}
        </p>
      </aside>
    );
  }

  if (!detail) {
    return (
      <aside className="case">
        <p className="case__state">Konuşma seçin.</p>
      </aside>
    );
  }

  const supportCase = detail.case;

  return (
    <aside className="case">
      <header className="case__header">
        <h2>Vaka</h2>
        <span className={`mode-badge mode-badge--large ${botModeClass(detail.botMode)}`}>
          {botModeLabel(detail.botMode)}
        </span>
      </header>

      {detail.botReplyStatusLabel && (
        <p className="case__status">{detail.botReplyStatusLabel}</p>
      )}

      <section className="case__section">
        <h3>Durum</h3>
        <dl>
          <div>
            <dt>Mod</dt>
            <dd>{botModeLabel(detail.botMode)}</dd>
          </div>
          <div>
            <dt>Aşama</dt>
            <dd>{detail.stageLabel}</dd>
          </div>
          <div>
            <dt>Konuşma</dt>
            <dd>{detail.status}</dd>
          </div>
        </dl>
      </section>

      {supportCase && (
        <>
          {supportCase.issueSummary && (
            <section className="case__section">
              <h3>Sorun özeti</h3>
              <p>{supportCase.issueSummary}</p>
            </section>
          )}

          <section className="case__section">
            <h3>Ekran</h3>
            <p>{supportCase.screen?.displayText ?? "—"}</p>
          </section>

          <section className="case__section">
            <h3>Receiver</h3>
            <p>{supportCase.receiver?.count ?? "—"}</p>
          </section>

          <section className="case__section">
            <h3>Config bağlantısı</h3>
            <p>{supportCase.config?.displayText ?? "Bilinmiyor"}</p>
          </section>

          <section className="case__section">
            <h3>AnyDesk</h3>
            <p>{supportCase.anyDesk?.maskedAddress ?? "—"}</p>
          </section>

          <section className="case__section">
            <h3>Panel fotoğrafları</h3>
            <p>{supportCase.photos.length} adet</p>
            <div className="case__photos">
              {supportCase.photos.map((photo, index) => (
                <figure key={photo.id} className="case__photo">
                  <img
                    src={mediaUrl(photo.mediaUrl) ?? undefined}
                    alt={`Panel fotoğrafı ${index + 1}`}
                    loading="lazy"
                  />
                  <figcaption>#{index + 1}</figcaption>
                </figure>
              ))}
            </div>
          </section>

          {supportCase.summaryMarkdown && (
            <section className="case__section">
              <h3>Teknik özet</h3>
              <pre className="case__summary">{supportCase.summaryMarkdown}</pre>
            </section>
          )}
        </>
      )}

      <section className="case__controls">
        <h3>Kontroller</h3>
        {actionError && (
          <p className="case__error" role="alert">
            {actionError}
          </p>
        )}
        <button
          type="button"
          className="ctrl-btn ctrl-btn--human"
          disabled={actionLoading !== null}
          onClick={onTakeover}
        >
          {actionLoading === "takeover" ? "Devralınıyor…" : "Devral (Human)"}
        </button>
        <button
          type="button"
          className="ctrl-btn ctrl-btn--pause"
          disabled={actionLoading !== null}
          onClick={onPause}
        >
          {actionLoading === "pause" ? "Duraklatılıyor…" : "DURDUR"}
        </button>
        <button
          type="button"
          className="ctrl-btn ctrl-btn--bot"
          disabled={actionLoading !== null}
          onClick={onResume}
        >
          {actionLoading === "resume" ? "Devam ediliyor…" : "ÇALIŞTIR"}
        </button>
      </section>
    </aside>
  );
}
