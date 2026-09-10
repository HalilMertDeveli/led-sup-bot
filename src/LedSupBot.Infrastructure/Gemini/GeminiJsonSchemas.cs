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
                @enum = new[] { "unknown", "greeting", "technical_support", "confirmation", "rejection", "ask_clarification", "ask_technical_question", "correction", "acknowledgement" },
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
            customerQuestion = new { type = "STRING", nullable = true },
            shouldAnswerQuestion = new { type = "BOOLEAN", nullable = true },
            shouldContinueWorkflowNow = new { type = "BOOLEAN", nullable = true },
            shouldStartNewCase = new { type = "BOOLEAN", nullable = true },
            needsClarification = new { type = "BOOLEAN", nullable = true },
            suggestedClarification = new { type = "STRING", nullable = true },
            answerConfidence = new { type = "NUMBER", nullable = true },
            diagnosticHypothesis = new { type = "STRING", nullable = true },
            issueSummary = new { type = "STRING", nullable = true },
            needsHuman = new { type = "BOOLEAN" },
            suggestedNextAction = new { type = "STRING", nullable = true },
            aiOperatorSummary = new { type = "STRING", nullable = true },
        },
        required = new[] { "intent", "reply", "needsHuman" },
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
            model = new { type = "STRING", nullable = true },
            manufacturer = new { type = "STRING", nullable = true },
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
            clarificationRequested = new { type = "BOOLEAN", nullable = true },
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
