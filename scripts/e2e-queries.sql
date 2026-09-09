SELECT COUNT(*) FROM messages WHERE "ExternalMessageId" = 'e2e-final-001';
SELECT "Direction", "SentBy" FROM messages WHERE "ExternalMessageId" = 'e2e-final-001';
SELECT "SentBy", "Direction" FROM messages WHERE "Content" = 'DASHBOARD-TEST-E2E-001' ORDER BY "CreatedAt" DESC LIMIT 1;
SELECT "BotMode" FROM conversations WHERE "Id" = (SELECT "ConversationId" FROM messages WHERE "Content" = 'DASHBOARD-TEST-E2E-001' LIMIT 1);
