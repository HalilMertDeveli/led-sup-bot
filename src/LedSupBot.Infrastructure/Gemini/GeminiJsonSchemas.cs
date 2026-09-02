namespace LedSupBot.Infrastructure.Gemini;

internal static class GeminiJsonSchemas
{
    public static object Extraction => new
    {
        type = "OBJECT",
        properties = new
        {
            intent = new
            {
                type = "STRING",
                @enum = new[] { "unknown", "greeting", "technical_support", "confirmation", "rejection" },
            },
            screen = ScreenSchema(),
            receiver = ReceiverSchema(),
            config = ConfigSchema(),
            anyDesk = AnyDeskSchema(),
            ambiguities = new
            {
                type = "ARRAY",
                items = new { type = "STRING" },
            },
        },
        required = new[] { "intent", "screen", "receiver", "config", "anyDesk", "ambiguities" },
    };

    public static object ResponsePlan => new
    {
        type = "OBJECT",
        properties = new
        {
            intent = new { type = "STRING" },
            reply = new { type = "STRING" },
            issueSummary = new { type = "STRING" },
            needsHuman = new { type = "BOOLEAN" },
            suggestedNextAction = new { type = "STRING" },
        },
        required = new[] { "intent", "reply", "issueSummary", "needsHuman", "suggestedNextAction" },
    };

    private static object ScreenSchema() => new
    {
        type = "OBJECT",
        nullable = true,
        properties = new
        {
            widthCm = NullableNumber(),
            heightCm = NullableNumber(),
            widthMeters = NullableNumber(),
            heightMeters = NullableNumber(),
            confidence = new { type = "NUMBER" },
            unitAmbiguous = new { type = "BOOLEAN" },
        },
        required = new[] { "widthCm", "heightCm", "widthMeters", "heightMeters", "confidence", "unitAmbiguous" },
    };

    private static object ReceiverSchema() => new
    {
        type = "OBJECT",
        nullable = true,
        properties = new
        {
            count = new { type = "INTEGER", nullable = true },
            confidence = new { type = "NUMBER" },
        },
        required = new[] { "count", "confidence" },
    };

    private static object ConfigSchema() => new
    {
        type = "OBJECT",
        nullable = true,
        properties = new
        {
            connected = new { type = "BOOLEAN", nullable = true },
            confirmed = new { type = "BOOLEAN", nullable = true },
            confidence = new { type = "NUMBER" },
        },
        required = new[] { "connected", "confirmed", "confidence" },
    };

    private static object AnyDeskSchema() => new
    {
        type = "OBJECT",
        nullable = true,
        properties = new
        {
            address = new { type = "STRING", nullable = true },
            confidence = new { type = "NUMBER" },
        },
        required = new[] { "address", "confidence" },
    };

    private static object NullableNumber() => new { type = "NUMBER", nullable = true };
}
