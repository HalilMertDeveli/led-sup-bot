// Dev: empty base → Vite proxies /api and /hubs to the ASP.NET API (no CORS issues).
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "";

export const config = {
  apiBaseUrl: apiBaseUrl.replace(/\/$/, ""),
} as const;
