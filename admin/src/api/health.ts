import { config } from "../config";

export type ApiHealth = {
  status: string;
};

export async function fetchApiHealth(signal?: AbortSignal): Promise<ApiHealth> {
  const response = await fetch(`${config.apiBaseUrl}/health`, { signal });
  if (!response.ok) {
    throw new Error(`Health check failed with HTTP ${response.status}`);
  }
  return (await response.json()) as ApiHealth;
}
