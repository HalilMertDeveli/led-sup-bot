import { useEffect, useState } from "react";
import { fetchAuthStatus } from "./api/admin";
import { CompactPanel } from "./components/CompactPanel";
import { Dashboard } from "./components/Dashboard";
import { LoginPage } from "./components/LoginPage";
import "./App.css";

function useHashRoute() {
  const [route, setRoute] = useState(() => window.location.hash.replace(/^#\/?/, "") || "dashboard");

  useEffect(() => {
    const onHash = () => setRoute(window.location.hash.replace(/^#\/?/, "") || "dashboard");
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  return route;
}

function App() {
  const [checking, setChecking] = useState(true);
  const [authenticated, setAuthenticated] = useState(false);
  const [configured, setConfigured] = useState(true);
  const route = useHashRoute();

  useEffect(() => {
    fetchAuthStatus()
      .then((status) => {
        setConfigured(status.configured);
        setAuthenticated(status.authenticated);
      })
      .catch(() => {
        setConfigured(false);
        setAuthenticated(false);
      })
      .finally(() => setChecking(false));
  }, []);

  if (checking) {
    return (
      <div className="login">
        <p className="login__state">Oturum kontrol ediliyor…</p>
      </div>
    );
  }

  if (!authenticated) {
    return (
      <LoginPage configured={configured} onSuccess={() => setAuthenticated(true)} />
    );
  }

  if (route === "compact") {
    return (
      <CompactPanel
        onLogout={() => setAuthenticated(false)}
        onOpenFull={() => {
          window.location.hash = "#/";
        }}
      />
    );
  }

  return <Dashboard onLogout={() => setAuthenticated(false)} />;
}

export default App;
