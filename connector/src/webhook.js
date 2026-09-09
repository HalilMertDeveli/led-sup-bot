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
  const retryDelaysMs = [0, 250, 1000, 2500, 5000];

  for (let attempt = 1; attempt <= retryDelaysMs.length; attempt += 1) {
    if (retryDelaysMs[attempt - 1] > 0) await delay(retryDelaysMs[attempt - 1]);
    try {
      const response = await fetch(config.aspNetWebhookUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Connector-Secret": config.sharedSecret,
        },
        body: JSON.stringify(payload),
      });

      if (response.ok) {
        logger.info({
          event: "EVENT_FORWARD_SUCCESS",
          stage: "API_EVENT_ACCEPTED",
          correlationId: event?.correlationId ?? null,
          eventType: event.eventType,
          messageId,
          httpStatus: response.status,
          attempt,
          durationMs: Date.now() - startedAt,
        }, "EVENT_FORWARD_SUCCESS");
        return true;
      }

      const retryable = response.status === 429 || response.status >= 500;
      logger.warn({
        event: "EVENT_FORWARD_FAILED",
        failureStage: "CONNECTOR_TO_API",
        failureCategory: retryable ? "HTTP_RETRYABLE" : "HTTP_REJECTED",
        correlationId: event?.correlationId ?? null,
        eventType: event.eventType,
        messageId,
        httpStatus: response.status,
        attempt,
        durationMs: Date.now() - startedAt,
      }, "ASP.NET webhook rejected event");
      if (!retryable) return false;
    } catch (error) {
      logger.warn({
        event: "EVENT_FORWARD_FAILED",
        failureStage: "CONNECTOR_TO_API",
        failureCategory: error?.name ?? "NETWORK",
        correlationId: event?.correlationId ?? null,
        eventType: event.eventType,
        messageId,
        attempt,
        durationMs: Date.now() - startedAt,
      }, "ASP.NET webhook unreachable");
    }
  }

  return false;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
