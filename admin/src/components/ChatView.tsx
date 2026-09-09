import { mediaUrl } from "../api/client";
import type { MessageItem } from "../types";
import { isImageMessage, toChatBubble } from "../utils/messagePresentation";

interface ChatViewProps {
  messages: MessageItem[];
  loading: boolean;
  error: string | null;
  draft: string;
  sending: boolean;
  onDraftChange: (value: string) => void;
  onSend: () => void;
}

export function ChatView({
  messages,
  loading,
  error,
  draft,
  sending,
  onDraftChange,
  onSend,
}: ChatViewProps) {
  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      onSend();
    }
  }

  return (
    <section className="chat">
      <header className="chat__header">
        <h2>Sohbet</h2>
      </header>

      <div className="chat__messages" aria-live="polite">
        {loading && messages.length === 0 && <p className="chat__state">Mesajlar yükleniyor…</p>}
        {error && (
          <p className="chat__error" role="alert">
            {error}
          </p>
        )}
        {!loading && messages.length === 0 && !error && (
          <p className="chat__state">Henüz mesaj yok.</p>
        )}
        {messages.map((raw) => {
          const bubble = toChatBubble(raw);
          return (
            <article
              key={bubble.id}
              className={`chat__bubble chat__bubble--${bubble.side}`}
              data-testid={`chat-bubble-${bubble.side}`}
            >
              {bubble.label && <span className="chat__label">{bubble.label}</span>}
              {isImageMessage(bubble.messageType) && bubble.mediaUrl ? (
                <img
                  className="chat__image"
                  src={mediaUrl(bubble.mediaUrl) ?? undefined}
                  alt="Gönderilen fotoğraf"
                  loading="lazy"
                />
              ) : null}
              {bubble.content && <p className="chat__text">{bubble.content}</p>}
              {!bubble.content && isImageMessage(bubble.messageType) && !bubble.mediaUrl && (
                <p className="chat__text chat__text--muted">📷 Fotoğraf</p>
              )}
              <time className="chat__time" dateTime={bubble.createdAt}>
                {formatTime(bubble.createdAt)}
              </time>
            </article>
          );
        })}
      </div>

      <footer className="chat__composer">
        <textarea
          rows={2}
          placeholder="Mesaj yazın…"
          value={draft}
          onChange={(e) => onDraftChange(e.target.value)}
          onKeyDown={handleKeyDown}
          disabled={sending}
        />
        <button type="button" onClick={onSend} disabled={sending || !draft.trim()}>
          {sending ? "Gönderiliyor…" : "Gönder"}
        </button>
      </footer>
    </section>
  );
}

function formatTime(iso: string): string {
  try {
    return new Intl.DateTimeFormat("tr-TR", {
      hour: "2-digit",
      minute: "2-digit",
      day: "2-digit",
      month: "2-digit",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}
