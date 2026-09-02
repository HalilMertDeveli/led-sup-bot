using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.OpenAi;

public sealed class OpenAiConversationPlannerService : IAiConversationPlanner
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiConversationPlannerService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAiConversationPlannerService(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiConversationPlannerService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiResponsePlanRaw?> TryPlanAsync(
        ConversationContext context,
        string latestCustomerMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return null;
        }

        var payload = new ChatCompletionRequest
        {
            Model = _options.Model,
            Temperature = 0.2,
            Messages =
            [
                new ChatMessage("system", BuildSystemPrompt()),
                new ChatMessage("user", BuildUserPrompt(context, latestCustomerMessage)),
            ],
            ResponseFormat = new ResponseFormatWrapper
            {
                Type = "json_schema",
                JsonSchema = new JsonSchemaWrapper
                {
                    Name = "response_plan",
                    Strict = true,
                    Schema = ResponsePlanJsonSchema.Document,
                },
            },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI response plan failed with status {StatusCode}", (int)response.StatusCode);
            return null;
        }

        var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ResponsePlanPayload>(content, JsonOptions);
            if (parsed is null)
            {
                return null;
            }

            return new AiResponsePlanRaw(
                parsed.Intent ?? "unknown",
                parsed.Reply,
                parsed.IssueSummary,
                parsed.NeedsHuman,
                parsed.SuggestedNextAction);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenAI response plan JSON invalid");
            return null;
        }
    }

    private static string BuildSystemPrompt() =>
        """
        You are communicating with customers through WhatsApp as a technical support technician for LED display systems.
        Your messages must feel like they were written by an experienced, practical, and calm technical support employee on WhatsApp, NOT by an AI assistant or corporate chatbot.

        Target Personality & Tone:
        - Friendly but professional, calm, practical, mildly informal, natural, technically competent, patient, concise.
        - The customer should feel they are talking to a real, experienced LED technician on WhatsApp.
        - Do NOT sound corporate, robotic, academic, or overly polite.
        - NO EMOJIS by default.
        - Messages should be SHORT (1 to 3 short sentences).
        - Use natural everyday Turkish: "Tamam, buna bakalım.", "Anladım.", "Burada önce receiver tarafını kontrol edelim.", "Önce en kolay ihtimali eleyelim."
        - Avoid AI/corporate clichés: "Size yardımcı olmaktan memnuniyet duyarım.", "Harika bir soru.", "Detaylı bir şekilde açıklayayım.", "Kesinlikle!", "Elbette!"
        - Do NOT over-acknowledge or parrot what customer just said.
        - Ask ONE useful question at a time.
        - If customer asks questions, answer simply and directly FIRST.
        - Never re-ask for information listed in knownFacts.
        - NEVER promise that a technical team will call/contact the customer.
        - Ultimate Filter: "Would a real LED support technician realistically send this message on WhatsApp?"
        """;

    private static string BuildUserPrompt(ConversationContext context, string latestCustomerMessage)
    {
        var history = context.RecentMessages.Count == 0
            ? "none"
            : string.Join("\n", context.RecentMessages.Select(m => $"- {m.Role}: {m.Content}"));

        return $"""
            botMode: {context.BotMode}
            currentStage: {context.CurrentStage} ({context.StageLabel})
            currentIssue: {context.CurrentIssue ?? "none"}
            knownFacts: {(context.KnownFacts.Count == 0 ? "none" : string.Join("; ", context.KnownFacts))}
            missingFacts: {(context.MissingFacts.Count == 0 ? "none" : string.Join("; ", context.MissingFacts))}
            screen: {context.Screen?.DisplayText ?? "missing"}
            receiver: {context.Receiver?.DisplayText ?? "missing"}
            panel: {context.Panel?.DisplayText ?? "missing"}
            config: {context.Config?.DisplayText ?? "missing"}
            anyDesk: {context.AnyDesk?.DisplayText ?? "missing"}
            recentMessages:
            {history}
            latestCustomerMessage:
            {latestCustomerMessage}
            """;
    }

    private sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = string.Empty;
        public double Temperature { get; set; }
        public List<ChatMessage> Messages { get; set; } = [];
        public ResponseFormatWrapper? ResponseFormat { get; set; }
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed class ResponseFormatWrapper
    {
        public string Type { get; set; } = string.Empty;
        public JsonSchemaWrapper JsonSchema { get; set; } = null!;
    }

    private sealed class JsonSchemaWrapper
    {
        public string Name { get; set; } = string.Empty;
        public bool Strict { get; set; }
        public object Schema { get; set; } = null!;
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public ChoiceMessage? Message { get; set; }
    }

    private sealed class ChoiceMessage
    {
        public string? Content { get; set; }
    }

    private sealed class ResponsePlanPayload
    {
        public string? Intent { get; set; }
        public string? Reply { get; set; }
        public string? IssueSummary { get; set; }
        public bool NeedsHuman { get; set; }
        public string? SuggestedNextAction { get; set; }
    }

    private static class ResponsePlanJsonSchema
    {
        public static readonly object Document = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                intent = new { type = "string" },
                reply = new { type = "string" },
                issueSummary = new { type = "string" },
                needsHuman = new { type = "boolean" },
                suggestedNextAction = new { type = "string" },
            },
            required = new[] { "intent", "reply", "issueSummary", "needsHuman", "suggestedNextAction" },
        };
    }
}
