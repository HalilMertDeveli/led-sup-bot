SELECT si."Width", si."Height", si."Unit" FROM screen_informations si
JOIN technical_support_cases t ON t."Id" = si."TechnicalSupportCaseId"
JOIN conversations c ON c."Id" = t."ConversationId"
JOIN customers cu ON cu."Id" = c."CustomerId"
WHERE cu."PhoneNumber" = '905559990011';
