import { useCallback, useEffect, useRef, useState } from "react";
import {
  fetchConversation,
  fetchConversations,
  fetchMessages,
  fetchSystemStatus,
  logout,
  pause,
  resumeBot,
  sendMessage,
  takeover,
} from "../api/admin";
import { ApiError } from "../api/client";
import type { ConversationDetail, ConversationListItem, MessageItem } from "../types";
import { CasePanel } from "./CasePanel";
import { ChatView } from "./ChatView";
import { ConversationList } from "./ConversationList";

const POLL_MS = 5000;

interface DashboardProps {
  onLogout: () => void;
}

export function Dashboard({ onLogout }: DashboardProps) {
  const [systemOnline, setSystemOnline] = useState<boolean | null>(null);
  const [items, setItems] = useState<ConversationListItem[]>([]);
  const [listLoading, setListLoading] = useState(true);
  const [listError, setListError] = useState<string | null>(null);
  const [filter, setFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ConversationDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [messages, setMessages] = useState<MessageItem[]>([]);
  const [messagesLoading, setMessagesLoading] = useState(false);
  const [messagesError, setMessagesError] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [confirmTakeover, setConfirmTakeover] = useState(false);
  const searchDebounce = useRef<ReturnType<typeof setTimeout> | null>(null);

  const loadList = useCallback(async () => {
    try {
      const data = await fetchConversations(filter, search);
      setItems(data.items);
      setListError(null);
    } catch (err) {
      setListError(err instanceof ApiError ? err.message : "Konuşmalar yüklenemedi.");
    } finally {
      setListLoading(false);
    }
  }, [filter, search]);

  const loadDetail = useCallback(async (id: string) => {
    setDetailLoading(true);
    setDetailError(null);
    try {
      const data = await fetchConversation(id);
      setDetail(data);
    } catch (err) {
      setDetail(null);
      setDetailError(err instanceof ApiError ? err.message : "Konuşma yüklenemedi.");
    } finally {
      setDetailLoading(false);
    }
  }, []);

  const loadMessages = useCallback(async (id: string) => {
    setMessagesLoading(true);
    setMessagesError(null);
    try {
      const data = await fetchMessages(id);
      setMessages(data.messages);
    } catch (err) {
      setMessages([]);
      setMessagesError(err instanceof ApiError ? err.message : "Mesajlar yüklenemedi.");
    } finally {
      setMessagesLoading(false);
    }
  }, []);

  const refreshSelected = useCallback(async () => {
    if (!selectedId) return;
    await Promise.all([loadDetail(selectedId), loadMessages(selectedId)]);
  }, [selectedId, loadDetail, loadMessages]);

  useEffect(() => {
    void loadList();
    fetchSystemStatus()
      .then(() => setSystemOnline(true))
      .catch(() => setSystemOnline(false));
  }, [loadList]);

  useEffect(() => {
    if (searchDebounce.current) clearTimeout(searchDebounce.current);
    searchDebounce.current = setTimeout(() => {
      setListLoading(true);
      void loadList();
    }, 300);
    return () => {
      if (searchDebounce.current) clearTimeout(searchDebounce.current);
    };
  }, [search, filter, loadList]);

  useEffect(() => {
    const timer = setInterval(() => {
      void loadList();
      void refreshSelected();
    }, POLL_MS);
    return () => clearInterval(timer);
  }, [loadList, refreshSelected]);

  useEffect(() => {
    if (!selectedId) {
      setDetail(null);
      setMessages([]);
      return;
    }
    void loadDetail(selectedId);
    void loadMessages(selectedId);
  }, [selectedId, loadDetail, loadMessages]);

  async function handleSend() {
    if (!selectedId || !draft.trim()) return;
    setSending(true);
    setMessagesError(null);
    try {
      await sendMessage(selectedId, draft.trim());
      setDraft("");
      await refreshSelected();
      await loadList();
    } catch (err) {
      setMessagesError(
        err instanceof ApiError
          ? err.status === 503
            ? "WhatsApp bağlantısı kullanılamıyor veya gönderim başarısız."
            : err.message
          : "Mesaj gönderilemedi.",
      );
    } finally {
      setSending(false);
    }
  }

  async function runAction(kind: "takeover" | "pause" | "resume") {
    if (!selectedId) return;
    setActionLoading(kind);
    setActionError(null);
    try {
      if (kind === "takeover") await takeover(selectedId);
      else if (kind === "pause") await pause(selectedId);
      else await resumeBot(selectedId);
      await refreshSelected();
      await loadList();
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "İşlem başarısız.");
    } finally {
      setActionLoading(null);
      setConfirmTakeover(false);
    }
  }

  function handleTakeoverClick() {
    if (!confirmTakeover) {
      setConfirmTakeover(true);
      return;
    }
    void runAction("takeover");
  }

  async function handleLogout() {
    try {
      await logout();
    } finally {
      onLogout();
    }
  }

  return (
    <div className="dashboard">
      <header className="topbar">
        <div className="topbar__brand">
          <span className="topbar__eyebrow">LED SUPPORT</span>
          <strong>Operasyon Paneli</strong>
        </div>
        <div className="topbar__status">
          <span className={`status-dot ${systemOnline ? "status-dot--online" : "status-dot--offline"}`} />
          {systemOnline === null ? "Kontrol ediliyor…" : systemOnline ? "Sistem çevrimiçi" : "API erişilemiyor"}
        </div>
        <button type="button" className="topbar__logout" onClick={() => void handleLogout()}>
          Çıkış
        </button>
      </header>

      <div className="dashboard__grid">
        <ConversationList
          items={items}
          selectedId={selectedId}
          loading={listLoading}
          error={listError}
          filter={filter}
          search={search}
          onFilterChange={setFilter}
          onSearchChange={setSearch}
          onSelect={setSelectedId}
        />

        {selectedId ? (
          <ChatView
            messages={messages}
            loading={messagesLoading}
            error={messagesError}
            draft={draft}
            sending={sending}
            onDraftChange={setDraft}
            onSend={() => void handleSend()}
          />
        ) : (
          <section className="chat chat--empty">
            <p className="chat__state">Sohbeti görmek için bir konuşma seçin.</p>
          </section>
        )}

        <CasePanel
          detail={detail}
          loading={detailLoading}
          error={detailError}
          actionLoading={actionLoading}
          actionError={actionError}
          onTakeover={handleTakeoverClick}
          onPause={() => void runAction("pause")}
          onResume={() => void runAction("resume")}
        />
      </div>

      {confirmTakeover && (
        <div className="modal-backdrop" role="dialog" aria-modal="true">
          <div className="modal">
            <h3>Devralmayı onayla</h3>
            <p>
              Bu konuşmayı devralmak istediğinizden emin misiniz?
              <br />
              Bot otomatik cevap göndermeyi durduracaktır.
            </p>
            <div className="modal__actions">
              <button type="button" onClick={() => setConfirmTakeover(false)}>
                İptal
              </button>
              <button type="button" className="ctrl-btn ctrl-btn--human" onClick={() => void runAction("takeover")}>
                Devral
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
