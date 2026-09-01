import pino from "pino";
import { config } from "./config.js";

/**
 * Structured logger. Never log QR payloads, session keys, or full message bodies.
 */
export const logger = pino({
  level: config.logLevel,
  redact: {
    paths: [
      "creds",
      "keys",
      "qr",
      "authState",
      "message.message",
      "text",
      "body",
      "textContent",
      "remotePhoneNumber",
      "sharedSecret",
      "authorization",
      "req.headers.authorization",
      "headers.authorization",
      "headers['X-Connector-Secret']",
      "headers.x-connector-secret",
    ],
    remove: true,
  },
});
