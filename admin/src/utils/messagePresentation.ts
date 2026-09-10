import type { BotMode, MessageDirection, MessageItem, MessageSender } from "../types";

/** Presentation-only mapping. Never exposes SentBy labels to the operator in chat UI. */
export type ChatSide = "incoming" | "outgoing" | "system";

export interface ChatBubble {
  id: string;
  side: ChatSide;
  content: string | null;
  messageType: MessageItem["messageType"];
  createdAt: string;
  mediaUrl: string | null;
  /** Optional minimal label for outgoing messages only */
  label: string | null;
}

export function toChatBubble(message: MessageItem): ChatBubble {
  const side = resolveSide(message.direction, message.sentBy);
  const messageType = normalizeMessageType(message.messageType);
  return {
    id: message.id,
    side,
    content: message.content,
    messageType,
    createdAt: message.createdAt,
    mediaUrl: message.mediaUrl,
    label: side === "outgoing" ? "Sen" : null,
  };
}

function resolveSide(direction: MessageDirection | number, sentBy: MessageSender | number): ChatSide {
  const normalizedSentBy = normalizeMessageSender(sentBy);
  const normalizedDirection = normalizeMessageDirection(direction);

  if (normalizedSentBy === "System") {
    return "system";
  }
  if (normalizedDirection === "Inbound" || normalizedSentBy === "Customer") {
    return "incoming";
  }
  return "outgoing";
}

export function botModeLabel(mode: BotMode | number | string): string {
  switch (normalizeBotMode(mode)) {
    case "Bot":
      return "BOT";
    case "Human":
      return "HUMAN";
    case "Paused":
      return "PAUSED";
    default:
      return String(mode).toUpperCase();
  }
}

export function botModeClass(mode: BotMode | number | string): string {
  switch (normalizeBotMode(mode)) {
    case "Bot":
      return "mode-bot";
    case "Human":
      return "mode-human";
    case "Paused":
      return "mode-paused";
    default:
      return "mode-unknown";
  }
}

export function normalizeBotMode(mode: BotMode | number | string): BotMode | string {
  if (typeof mode === "number") {
    switch (mode) {
      case 0:
        return "Bot";
      case 1:
        return "Paused";
      case 2:
        return "Human";
      default:
        return String(mode);
    }
  }
  return mode;
}

function normalizeMessageDirection(direction: MessageDirection | number): MessageDirection {
  if (typeof direction === "number") {
    return direction === 0 ? "Inbound" : "Outbound";
  }
  return direction;
}

function normalizeMessageSender(sentBy: MessageSender | number): MessageSender {
  if (typeof sentBy === "number") {
    switch (sentBy) {
      case 0:
        return "Bot";
      case 1:
        return "Technician";
      case 2:
        return "System";
      case 3:
        return "Customer";
      default:
        return "Customer";
    }
  }
  return sentBy;
}

function normalizeMessageType(messageType: MessageItem["messageType"] | number): MessageItem["messageType"] {
  if (typeof messageType === "number") {
    switch (messageType) {
      case 0:
        return "Text";
      case 1:
        return "Image";
      case 2:
        return "Document";
      case 3:
        return "Audio";
      case 4:
        return "Video";
      case 5:
        return "Template";
      case 6:
        return "System";
      default:
        return "Other";
    }
  }
  return messageType;
}

export function isImageMessage(messageType: MessageItem["messageType"] | number): boolean {
  return normalizeMessageType(messageType) === "Image";
}
