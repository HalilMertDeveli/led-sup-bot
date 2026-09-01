import { config } from "./config.js";
import { logger } from "./logger.js";
import { messageObservabilityFields } from "./observability.js";

export async function forwardEventToAspNet(event) {
  const payload = {
    ...event,
    // never include session material
  };

  const messageId = event?.externalMessageId ?? null;
  const startedAt = Date.now();

  try {
    const response = await fetch(config.aspNetWebhookUrl, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Connector-Secret": config.sharedSecret,
      },
      body: JSON.stringify(payload),
    });

    const durationMs = Date.now() - startedAt;

    if (!response.ok) {
      logger.warn(
        {
          event: "EVENT_FORWARD_FAILED",
          eventType: event.eventType,
          messageId,
          httpStatus: response.status,
          durationMs,
        },
        "ASP.NET webhook rejected event",
      );
      return false;
    }

    logger.info(
      {
        event: "EVENT_FORWARD_SUCCESS",
        eventType: event.eventType,
        messageId,
        httpStatus: response.status,
        durationMs,
      },
      "EVENT_FORWARD_SUCCESS",
    );

    return true;
  } catch (error) {
    logger.warn(
      {
        event: "EVENT_FORWARD_FAILED",
        err: error?.message,
        eventType: event.eventType,
        messageId,
        durationMs: Date.now() - startedAt,
      },
      "ASP.NET webhook unreachable",
    );
    return false;
  }
}
