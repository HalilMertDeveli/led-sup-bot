using System.Text.Json;
using System.Text.Json.Serialization;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Infrastructure.Gemini;

public sealed class GeminiConversationPlannerService : IAiConversationPlanner
{
    private readonly GeminiStructuredClient _client;
    private readonly ILogger<GeminiConversationPlannerService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GeminiConversationPlannerService(
        GeminiStructuredClient client,
        ILogger<GeminiConversationPlannerService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AiResponsePlanRaw?> TryPlanAsync(
        ConversationContext context,
        string latestCustomerMessage,
        CancellationToken cancellationToken = default)
    {
        var content = await _client.TryGenerateJsonAsync(
            "response_plan",
            BuildSystemPrompt(),
            BuildUserPrompt(context, latestCustomerMessage),
            GeminiJsonSchemas.ResponsePlan,
            temperature: 0.2,
            cancellationToken);

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
            _logger.LogWarning(ex, "Gemini response plan JSON invalid; using deterministic fallback");
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
        - If customer asks how to connect the controller/device to their computer or which cable to use, explain clearly using USB-A and USB-B printer-style cable guidance: "Kutunun içinden çıkan yazıcı tipi USB kablosunu kullanabilirsiniz. Kablonun kareye yakın USB-B ucunu cihaza, standart USB-A ucunu bilgisayarınıza takın."
        - NEVER promise that a technical team or another person will call/contact the customer (e.g. never say "ekibimiz sizinle iletişime geçecek", "sizi arayacaklar", etc.).
        - Keep reply concise, professional, Turkish.
        - Do not invent collected technical data.
        - needsHuman=true only for explicit escalation requests; server decides handoff.
        - suggestedNextAction is advisory only (e.g. ask_screen, ask_panel, ask_anydesk, acknowledge_issue).
        - botMode in context is read-only; never instruct mode changes.
        - Do not include secrets, phone numbers, full JIDs, or AnyDesk credentials in replies.
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

    private sealed class ResponsePlanPayload
    {
        public string? Intent { get; set; }
        public string? Reply { get; set; }
        public string? IssueSummary { get; set; }
        public bool NeedsHuman { get; set; }
        public string? SuggestedNextAction { get; set; }
    }
}
