SELECT "SentBy", "DeliveryStatus", LEFT("Content",40), "ExternalMessageId", "ConversationId"
FROM messages WHERE "ExternalMessageId" IN ('SELF-ROUTE-TEST-001') OR "CreatedAt" > NOW() - INTERVAL '2 minutes'
ORDER BY "CreatedAt" DESC LIMIT 6;
SELECT "LastInboundWhatsAppAddress", "LinkedWhatsAppLid" FROM conversations c
JOIN customers cu ON cu."Id"=c."CustomerId" WHERE c."Id"='f35226ab-99c8-459c-8182-7dbc5f435814';
SELECT "LinkedWhatsAppLid" FROM customers WHERE "PhoneNumber"='905425192119';
