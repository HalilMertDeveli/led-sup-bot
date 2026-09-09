import { describe, expect, it } from "vitest";
import { toChatBubble } from "./messagePresentation";
import type { MessageItem } from "../types";

function msg(partial: Partial<MessageItem> & Pick<MessageItem, "direction" | "sentBy">): MessageItem {
  return {
    id: "1",
    messageType: "Text",
    content: "hello",
    deliveryStatus: "Delivered",
    createdAt: "2026-01-01T00:00:00Z",
    storageKey: null,
    mediaUrl: null,
    ...partial,
  };
}

describe("toChatBubble", () => {
  it("maps customer inbound to incoming without sender badge", () => {
    const bubble = toChatBubble(msg({ direction: "Inbound", sentBy: "Customer" }));
    expect(bubble.side).toBe("incoming");
    expect(bubble.label).toBeNull();
  });

  it("maps bot outbound to outgoing with Sen label only", () => {
    const bubble = toChatBubble(msg({ direction: "Outbound", sentBy: "Bot" }));
    expect(bubble.side).toBe("outgoing");
    expect(bubble.label).toBe("Sen");
  });

  it("maps technician outbound to outgoing with Sen label only", () => {
    const bubble = toChatBubble(msg({ direction: "Outbound", sentBy: "Technician" }));
    expect(bubble.side).toBe("outgoing");
    expect(bubble.label).toBe("Sen");
  });

  it("does not expose internal SentBy in label for customer", () => {
    const bubble = toChatBubble(msg({ direction: "Inbound", sentBy: "Customer" }));
    expect(bubble.label).toBeNull();
  });
});

describe("botModeLabel", () => {
  it("handles numeric enum values from API", async () => {
    const { botModeLabel } = await import("./messagePresentation");
    expect(botModeLabel(0)).toBe("BOT");
    expect(botModeLabel(1)).toBe("PAUSED");
    expect(botModeLabel(2)).toBe("HUMAN");
  });
});
