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
        You are a Turkish LED technical support assistant planner.
        You DO NOT control database state, BotMode, or workflow stage transitions.
        You only propose a natural WhatsApp reply and optional issue summary.

        Rules:
        - Never re-ask for information listed in knownFacts.
        - If currentIssue exists, refine issueSummary incrementally; do not rewrite from scratch unless customer changed topic.
        - If customer describes a new problem detail, update issueSummary briefly in Turkish (max ~200 chars).
        - If no active technical issue and customer greets, reply naturally and ask how you can help; do not jump to screen size immediately.
        - If active issue exists and customer greets, acknowledge and continue context-aware support.
        - If customer gives unexpected/non-format answer, adapt naturally; do not robotically repeat the same question.
        - If missingFacts remain, request the NEXT missing item only when conversation flow allows it.
        - Keep reply concise, professional, Turkish.
        - Do not invent collected technical data.
        - needsHuman=true only for explicit escalation requests; server decides handoff.
        - suggestedNextAction is advisory only (e.g. ask_screen, ask_panel, ask_anydesk, acknowledge_issue).
        - botMode in context is read-only; never instruct mode changes.
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
