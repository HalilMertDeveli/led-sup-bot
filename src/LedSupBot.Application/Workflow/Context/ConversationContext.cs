using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Workflow.Context;

public sealed record ContextMessage(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record ContextScreen(string? DisplayText, bool Complete);

public sealed record ContextReceiver(string? DisplayText, bool Complete);

public sealed record ContextPanel(string DisplayText, bool Complete);

public sealed record ContextConfig(string DisplayText, bool Complete);

public sealed record ContextAnyDesk(string DisplayText, bool Complete);

public sealed class ConversationContext
{
    public Guid ConversationId { get; init; }

    public ConversationStage CurrentStage { get; init; }

    public string StageLabel { get; init; } = string.Empty;

    public IReadOnlyList<ContextMessage> RecentMessages { get; init; } = [];

    public string? CurrentIssue { get; init; }

    public IReadOnlyList<string> KnownFacts { get; init; } = [];

    public IReadOnlyList<string> MissingFacts { get; init; } = [];

    public ContextScreen? Screen { get; init; }

    public ContextReceiver? Receiver { get; init; }

    public ContextPanel? Panel { get; init; }

    public ContextConfig? Config { get; init; }

    public ContextAnyDesk? AnyDesk { get; init; }
}
