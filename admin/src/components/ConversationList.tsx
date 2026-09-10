import type { ConversationListItem } from "../types";
import { botModeClass, botModeLabel } from "../utils/messagePresentation";

interface ConversationListProps {
  items: ConversationListItem[];
  selectedId: string | null;
  loading: boolean;
  error: string | null;
  filter: string;
  search: string;
  onFilterChange: (filter: string) => void;
  onSearchChange: (search: string) => void;
  onSelect: (id: string) => void;
}

const FILTERS = [
  { id: "all", label: "Tümü" },
  { id: "bot", label: "Bot" },
  { id: "human", label: "Human" },
  { id: "paused", label: "Paused" },
];

export function ConversationList({
  items,
  selectedId,
  loading,
  error,
  filter,
  search,
  onFilterChange,
  onSearchChange,
  onSelect,
}: ConversationListProps) {
  return (
    <aside className="sidebar">
      <header className="sidebar__header">
        <h2>Konuşmalar</h2>
      </header>

      <div className="sidebar__filters">
        {FILTERS.map((f) => (
          <button
            key={f.id}
            type="button"
            className={filter === f.id ? "filter-btn filter-btn--active" : "filter-btn"}
            onClick={() => onFilterChange(f.id)}
          >
            {f.label}
          </button>
        ))}
      </div>

      <input
        className="sidebar__search"
        type="search"
        placeholder="Ara…"
        value={search}
        onChange={(e) => onSearchChange(e.target.value)}
        aria-label="Konuşma ara"
      />

      {error && (
        <p className="sidebar__error" role="alert">
          {error}
        </p>
      )}

      <ul className="sidebar__list" aria-busy={loading}>
        {loading && items.length === 0 && <li className="sidebar__state">Yükleniyor…</li>}
        {!loading && items.length === 0 && !error && (
          <li className="sidebar__state">Konuşma bulunamadı.</li>
        )}
        {items.map((item) => (
          <li key={item.id}>
            <button
              type="button"
              className={
                selectedId === item.id ? "conv-row conv-row--selected" : "conv-row"
              }
              onClick={() => onSelect(item.id)}
            >
              <div className="conv-row__top">
                <span className="conv-row__name">{item.customerIdentifier}</span>
                <span className={`mode-badge ${botModeClass(item.botMode)}`}>
                  {botModeLabel(item.botMode)}
                </span>
              </div>
              <p className="conv-row__preview">{item.lastMessagePreview ?? "—"}</p>
              <div className="conv-row__meta">
                <span>{item.stageLabel}</span>
                <time dateTime={item.updatedAt}>{formatRelative(item.updatedAt)}</time>
                {item.requiresAttention && (
                  <span className="conv-row__attention" title="Dikkat gerektiriyor">
                    ●
                  </span>
                )}
              </div>
            </button>
          </li>
        ))}
      </ul>
    </aside>
  );
}

function formatRelative(iso: string): string {
  try {
    const date = new Date(iso);
    const diff = Date.now() - date.getTime();
    if (diff < 60_000) return "az önce";
    if (diff < 3_600_000) return `${Math.floor(diff / 60_000)} dk`;
    if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)} sa`;
    return new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit" }).format(date);
  } catch {
    return iso;
  }
}
