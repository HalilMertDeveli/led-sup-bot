-- E2E-12
SELECT c."BotMode", c."Status", c."CurrentStage", m."SentBy", m."Direction"
FROM messages m
JOIN conversations c ON c."Id" = m."ConversationId"
WHERE m."ExternalMessageId" = 'e2e-final-012';

-- E2E-15 human block (no new bot messages after inbound while human)
SELECT COUNT(*) AS bot_outbound_count
FROM messages
WHERE "ConversationId" = (SELECT "ConversationId" FROM messages WHERE "ExternalMessageId" = 'e2e-final-015' LIMIT 1)
  AND "SentBy" = 'Bot'
  AND "CreatedAt" >= (SELECT "CreatedAt" FROM messages WHERE "ExternalMessageId" = 'e2e-final-015' LIMIT 1);

-- E2E-17 resume no greeting
SELECT COUNT(*) AS bot_messages FROM messages
WHERE "ConversationId" = (SELECT "Id" FROM conversations ORDER BY "StartedAt" DESC LIMIT 1)
  AND "SentBy" = 'Bot';

-- Screen for E2E-03 phone
SELECT si."Width", si."Height", si."Unit"
FROM screen_informations si
JOIN technical_support_cases t ON t."Id" = si."TechnicalSupportCaseId"
JOIN conversations c ON c."Id" = t."ConversationId"
JOIN customers cu ON cu."Id" = c."CustomerId"
WHERE cu."PhoneNumber" = '905559990010';

-- Ambiguous phone 905559990002
SELECT COUNT(*) FROM screen_informations si
JOIN technical_support_cases t ON t."Id" = si."TechnicalSupportCaseId"
JOIN conversations c ON c."Id" = t."ConversationId"
JOIN customers cu ON cu."Id" = c."CustomerId"
WHERE cu."PhoneNumber" = '905559990002';
