SELECT m."ExternalMessageId", m."SentBy", m."Direction", LEFT(m."Content", 60) AS content
FROM "Messages" m
WHERE m."ExternalMessageId" IN ('TEST-ALLOW-002', 'TEST-BLOCK-002')
   OR m."CreatedAt" > NOW() - INTERVAL '5 minutes'
ORDER BY m."CreatedAt" DESC
LIMIT 15;
