import { useState, type FormEvent } from "react";
import { login } from "../api/admin";
import { ApiError } from "../api/client";

interface LoginPageProps {
  onSuccess: () => void;
  configured: boolean;
}

export function LoginPage({ onSuccess, configured }: LoginPageProps) {
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await login(password);
      onSuccess();
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.status === 401
            ? "Geçersiz parola."
            : err.status === 503
              ? "Admin kimlik doğrulaması API'de yapılandırılmamış."
              : err.message
          : err instanceof Error && err.message.includes("fetch")
            ? "API'ye bağlanılamadı. localhost:5080 çalışıyor mu?"
            : "Giriş yapılamadı. API çalışıyor mu?";
      setError(message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login">
      <div className="login__card">
        <p className="login__eyebrow">LED Support</p>
        <h1>Teknisyen Girişi</h1>
        <p className="login__hint">Yerel operasyon paneli — yalnızca yetkili personel.</p>
        {import.meta.env.DEV && configured && (
          <p className="login__hint">
            Yerel geliştirme parolası: <code>led-sup-admin</code>
          </p>
        )}
        {!configured && (
          <p className="login__warn" role="alert">
            Sunucuda admin kimlik doğrulaması yapılandırılmamış. API ortamında{" "}
            <code>ADMIN_DASHBOARD_PASSWORD</code> ve <code>ADMIN_SESSION_SECRET</code> ayarlayın.
          </p>
        )}
        <form onSubmit={handleSubmit}>
          <label htmlFor="password">Parola</label>
          <input
            id="password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={loading || !configured}
            required
          />
          {error && (
            <p className="login__error" role="alert">
              {error}
            </p>
          )}
          <button type="submit" disabled={loading || !configured}>
            {loading ? "Giriş yapılıyor…" : "Giriş"}
          </button>
        </form>
      </div>
    </div>
  );
}
