namespace LedSupBot.Infrastructure.OpenAi;

internal static class ExtractionJsonSchema
{
    public static object Document => new
    {
        type = "object",
        properties = new
        {
            intent = new
            {
                type = "string",
                @enum = new[] { "unknown", "greeting", "technical_support", "confirmation", "rejection" },
            },
            screen = ScreenSchema(),
            receiver = ReceiverSchema(),
            player = PlayerSchema(),
            processorHdmi = ProcessorHdmiSchema(),
            config = ConfigSchema(),
            anyDesk = AnyDeskSchema(),
            ambiguities = new
            {
                type = "array",
                items = new { type = "string" },
            },
        },
        required = new[] { "intent", "screen", "receiver", "player", "processorHdmi", "config", "anyDesk", "ambiguities" },
        additionalProperties = false,
    };

    public static object Root => Document;

    private static object ScreenSchema() => new
    {
        type = new[] { "object", "null" },
        properties = new
        {
            widthCm = NullableNumber(),
            heightCm = NullableNumber(),
            widthMeters = NullableNumber(),
            heightMeters = NullableNumber(),
            confidence = new { type = "number" },
            unitAmbiguous = new { type = "boolean" },
        },
        required = new[] { "widthCm", "heightCm", "widthMeters", "heightMeters", "confidence", "unitAmbiguous" },
        additionalProperties = false,
    };

    private static object ReceiverSchema() => new
    {
        type = new[] { "object", "null" },
        properties = new
        {
            count = new { type = new[] { "integer", "null" } },
            model = new { type = new[] { "string", "null" } },
            confidence = new { type = "number" },
        },
        required = new[] { "count", "model", "confidence" },
        additionalProperties = false,
    };

    private static object PlayerSchema() => new
    {
        type = new[] { "object", "null" },
        properties = new
        {
            playerType = new { type = new[] { "string", "null" } },
            confidence = new { type = "number" },
        },
        required = new[] { "playerType", "confidence" },
        additionalProperties = false,
    };

    private static object ProcessorHdmiSchema() => new
    {
        type = new[] { "object", "null" },
        properties = new
        {
            connected = new { type = new[] { "boolean", "null" } },
            needsClarification = new { type = new[] { "boolean", "null" } },
            confidence = new { type = "number" },
        },
        required = new[] { "connected", "needsClarification", "confidence" },
        additionalProperties = false,
    };

    private static object ConfigSchema() => new
    {
        type = new[] { "object", "null" },
        properties = new
        {
            connected = new { type = new[] { "boolean", "null" } },
            confirmed = new { type = new[] { "boolean", "null" } },
            confidence = new { type = "number" },
        },
        required = new[] { "connected", "confirmed", "confidence" },
        additionalProperties = false,
    };

    private static object AnyDeskSchema() => new
    {
        type = new[] { "object", "null" },
        properties = new
        {
            address = new { type = new[] { "string", "null" } },
            confidence = new { type = "number" },
        },
        required = new[] { "address", "confidence" },
        additionalProperties = false,
    };

    private static object NullableNumber() => new { type = new[] { "number", "null" } };
}
