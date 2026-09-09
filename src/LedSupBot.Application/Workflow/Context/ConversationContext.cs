using LedSupBot.Domain.Enums;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.CaseMemory;
using LedSupBot.Application.Workflow.TechnicalAnswers;
using LedSupBot.Application.Workflow.Web;

namespace LedSupBot.Application.Workflow.Context;

public sealed record ContextMessage(
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record ContextScreen(string? DisplayText, bool Complete, bool Deferred = false);

public sealed record ContextReceiver(
    string? DisplayText,
    bool Complete,
    bool CountComplete = false,
    bool ModelComplete = false,
    int? Count = null,
    ReceiverModel? Model = null,
    bool CountDeferred = false,
    bool ModelDeferred = false);

public sealed record ContextPlayer(
    string? DisplayText,
    bool Complete,
    PlayerType? PlayerType = null,
    bool Deferred = false);

public sealed record ContextProcessorHdmi(
    string? DisplayText,
    bool Complete,
    bool? Connected = null,
    bool Deferred = false);

public sealed record ContextPanel(string DisplayText, bool Complete, bool Deferred = false);

public sealed record ContextConfig(string DisplayText, bool Complete, bool Deferred = false);

public sealed record ContextAnyDesk(string DisplayText, bool Complete, bool Deferred = false);

public enum PendingWorkflowFact
{
    None = 0,
    ScreenDimensions,
    PanelLabel,
    ReceiverCount,
    ReceiverModel,
    PlayerType,
    ProcessorHdmi,
    ConfigConnection,
    AnyDesk,
}

public sealed class ConversationContext
{
    public Guid CustomerId { get; init; }

    public Guid ConversationId { get; init; }

    public ConversationStage CurrentStage { get; init; }

    public string StageLabel { get; init; } = string.Empty;

    public IReadOnlyList<ContextMessage> RecentMessages { get; init; } = [];

    public string? LastBotQuestion { get; init; }

    public PendingWorkflowFact PendingFact { get; init; }

    public string? CurrentIssue { get; init; }

    public IReadOnlyList<string> KnownFacts { get; init; } = [];

    public IReadOnlyList<string> MissingFacts { get; init; } = [];

    public IReadOnlyList<string> AskableMissingFacts { get; init; } = [];

    public IReadOnlyList<string> DeferredMissingFacts { get; init; } = [];

    public ContextScreen? Screen { get; init; }

    public ContextReceiver? Receiver { get; init; }

    public ContextPlayer? Player { get; init; }

    public ContextProcessorHdmi? ProcessorHdmi { get; init; }

    public ContextPanel? Panel { get; init; }

    public ContextConfig? Config { get; init; }

    public ContextAnyDesk? AnyDesk { get; init; }

    public BotMode BotMode { get; init; }

    public SupportCaseStatus CurrentCaseStatus { get; init; } = SupportCaseStatus.CollectingInformation;

    public int CompletedCasesCount { get; init; }

    public string? PreviousCasesSummary { get; init; }

    public string? AiOperatorSummary { get; init; }

    public string? TechnicalKnowledgeContext { get; set; }

    public KnowledgeGroundingResult? KnowledgeGrounding { get; set; }

    public string? DiagnosticContext { get; set; }
    public CaseMemorySearchResult? CaseMemory { get; set; }
    public string? CaseMemoryContext { get; set; }
    public ApprovedTrainingSearchResult? ApprovedTraining { get; set; }
    public string? ApprovedTrainingContext { get; set; }
    public SameCustomerMemoryResult? SameCustomerMemory { get; set; }
    public string? SameCustomerMemoryContext { get; set; }
    public WebGroundingResult? WebGrounding { get; set; }
    public string? WebEvidenceContext { get; set; }
}
