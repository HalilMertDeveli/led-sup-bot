import { useEffect, useState } from "react";
import { fetchApiHealth } from "./api/health";
import { config } from "./config";
import "./App.css";

type ConnectionState = "checking" | "online" | "offline";

function App() {
  const [connection, setConnection] = useState<ConnectionState>("checking");
  const [detail, setDetail] = useState<string>("");

  useEffect(() => {
    const controller = new AbortController();

    fetchApiHealth(controller.signal)
      .then((health) => {
        setConnection("online");
        setDetail(health.status);
      })
      .catch((error: unknown) => {
        setConnection("offline");
        setDetail(error instanceof Error ? error.message : "API unreachable");
      });

    return () => controller.abort();
  }, []);

  return (
    <div className="shell">
      <header className="shell__header">
        <p className="shell__eyebrow">LED Support Bot</p>
        <h1>Admin foundation</h1>
        <p className="shell__lede">
          Phase 2 shell only. Full dashboard (queues, takeover, summaries) is Phase 9.
        </p>
      </header>

      <section className="panel">
        <h2>API connectivity</h2>
        <dl>
          <div>
            <dt>Base URL</dt>
            <dd>
              <code>{config.apiBaseUrl}</code>
            </dd>
          </div>
          <div>
            <dt>Health</dt>
            <dd data-state={connection}>{connection}</dd>
          </div>
          <div>
            <dt>Detail</dt>
            <dd>{detail || "—"}</dd>
          </div>
        </dl>
      </section>

      <section className="panel panel--muted">
        <h2>Not in this phase</h2>
        <ul>
          <li>WhatsApp Cloud API integration</li>
          <li>OpenAI extraction</li>
          <li>Conversation dashboard</li>
          <li>Human takeover controls</li>
        </ul>
      </section>
    </div>
  );
}

export default App;
