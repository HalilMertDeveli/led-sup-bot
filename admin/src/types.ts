export type BotMode = "Bot" | "Human" | "Paused" | 0 | 1 | 2;
export type ConversationStatus =
  | "Active"
  | "WaitingTechnician"
  | "InHumanSupport"
  | "Completed"
  | "Abandoned";
export type MessageDirection = "Inbound" | "Outbound";
export type MessageSender = "Bot" | "Technician" | "System" | "Customer";
export type MessageType = "Text" | "Image" | "Document" | "Audio" | "Video" | "Template" | "System" | "Other";

export interface ConversationListItem {
  id: string;
  customerIdentifier: string;
  displayName: string | null;
  status: ConversationStatus;
  botMode: BotMode;
  currentStage: string;
  stageLabel: string;
  lastMessagePreview: string | null;
  lastCustomerMessageAt: string | null;
  updatedAt: string;
  messageCount: number;
  requiresAttention: boolean;
}

export interface ConversationListResponse {
  items: ConversationListItem[];
  total: number;
}

export interface ScreenDetail {
  widthCm: number;
  heightCm: number;
  displayText: string;
}

export interface ReceiverDetail {
  count: number;
}

export interface ConfigDetail {
  status: string;
  exampleImageSent: boolean;
  displayText: string;
}

export interface AnyDeskDetail {
  maskedAddress: string;
}

export interface PhotoDetail {
  id: string;
  storageKey: string;
  contentType: string;
  sizeBytes: number | null;
  sortOrder: number;
  analysisStatus: string;
  mediaUrl: string;
}

export interface CaseDetail {
  id: string;
  status: string;
  summaryMarkdown: string | null;
  issueSummary: string | null;
  screen: ScreenDetail | null;
  receiver: ReceiverDetail | null;
  config: ConfigDetail | null;
  anyDesk: AnyDeskDetail | null;
  photos: PhotoDetail[];
}

export interface ConversationDetail {
  id: string;
  status: ConversationStatus;
  botMode: BotMode;
  currentStage: string;
  stageLabel: string;
  startedAt: string;
  closedAt: string | null;
  lastCustomerMessageAt: string | null;
  customer: { id: string; phoneNumber: string; displayName: string | null };
  case: CaseDetail | null;
  recentAudits: Array<{
    previousMode: BotMode;
    newMode: BotMode;
    source: string;
    changedAt: string;
  }>;
  botReplyStatusLabel: string | null;
}

export interface MessageItem {
  id: string;
  direction: MessageDirection;
  messageType: MessageType;
  content: string | null;
  sentBy: MessageSender;
  deliveryStatus: string;
  createdAt: string;
  storageKey: string | null;
  mediaUrl: string | null;
}

export interface MessagesResponse {
  messages: MessageItem[];
}

export interface SystemStatus {
  whatsAppConnection: string;
  database: string;
  openAiIntegration: string;
  workflowAutoReplyEnabled: boolean;
}

export interface CompactRecentMessage {
  role: string;
  content: string;
  createdAt: string;
}

export interface CompactFocus {
  conversationId: string | null;
  botMode: BotMode | null;
  botReplyStatusLabel: string | null;
  customerDisplayName: string | null;
  issueSummary: string | null;
  currentStage?: string | null;
  stageLabel?: string | null;
  screenSummary: string | null;
  receiverSummary: string | null;
  panelStatus: string;
  configStatus: string;
  anyDeskStatus: string;
  photoCount?: number;
  showAnyDeskAlert: boolean;
  recentMessages: CompactRecentMessage[];
}

export interface TakeoverResponse {
  changed: boolean;
  botMode: BotMode;
  stage: string;
  status: ConversationStatus;
}
