const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

export const config = {
  apiBaseUrl: apiBaseUrl.replace(/\/$/, ""),
} as const;
