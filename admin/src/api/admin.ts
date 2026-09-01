import type {
  CompactFocus,
  ConversationDetail,
  ConversationListResponse,
  MessagesResponse,
  SystemStatus,
  TakeoverResponse,
} from "../types";
import { apiFetch } from "./client";

export interface AuthStatus {
  configured: boolean;
  authenticated: boolean;
}

export async function fetchAuthStatus(): Promise<AuthStatus> {
  return apiFetch<AuthStatus>("/api/admin/auth/status");
}

export async function login(password: string): Promise<void> {
  await apiFetch("/api/admin/auth/login", {
    method: "POST",
    body: JSON.stringify({ password }),
  });
}

export async function logout(): Promise<void> {
  await apiFetch("/api/admin/auth/logout", { method: "POST" });
}

export async function fetchSystemStatus(): Promise<SystemStatus> {
  return apiFetch<SystemStatus>("/api/admin/system/status");
}

export async function fetchConversations(
  mode?: string,
  search?: string,
): Promise<ConversationListResponse> {
  const params = new URLSearchParams();
  if (mode && mode !== "all") params.set("mode", mode);
  if (search?.trim()) params.set("search", search.trim());
  const qs = params.toString();
  return apiFetch<ConversationListResponse>(`/api/admin/conversations${qs ? `?${qs}` : ""}`);
}

export async function fetchConversation(id: string): Promise<ConversationDetail> {
  return apiFetch<ConversationDetail>(`/api/admin/conversations/${id}`);
}

export async function fetchMessages(id: string): Promise<MessagesResponse> {
  return apiFetch<MessagesResponse>(`/api/admin/conversations/${id}/messages`);
}

export async function sendMessage(id: string, text: string): Promise<{ messageId: string }> {
  return apiFetch(`/api/admin/conversations/${id}/messages`, {
    method: "POST",
    body: JSON.stringify({ text }),
  });
}

export async function takeover(id: string): Promise<TakeoverResponse> {
  return apiFetch(`/api/admin/conversations/${id}/takeover`, { method: "POST" });
}

export async function pause(id: string): Promise<TakeoverResponse> {
  return apiFetch(`/api/admin/conversations/${id}/pause`, { method: "POST" });
}

export async function resumeBot(id: string): Promise<TakeoverResponse> {
  return apiFetch(`/api/admin/conversations/${id}/resume`, { method: "POST" });
}

export async function fetchCompactFocus(): Promise<CompactFocus> {
  return apiFetch<CompactFocus>("/api/admin/compact/focus");
}

export async function dismissAnyDeskAlert(id: string): Promise<void> {
  await apiFetch(`/api/admin/conversations/${id}/dismiss-anydesk-alert`, { method: "POST" });
}
