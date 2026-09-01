using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Takeover;

public sealed class ConversationTakeoverService : IConversationTakeoverService
{
    private readonly IApplicationDbContext _db;
    private readonly WorkflowOptions _workflowOptions;
    private readonly IAdminEventNotifier _notifier;
    private readonly IConversationReplyCoordinator? _replyCoordinator;
    private readonly ILogger<ConversationTakeoverService> _logger;

    public ConversationTakeoverService(
        IApplicationDbContext db,
        IOptions<WorkflowOptions> workflowOptions,
        IAdminEventNotifier notifier,
        ILogger<ConversationTakeoverService> logger,
        IConversationReplyCoordinator? replyCoordinator = null)
    {
        _db = db;
        _workflowOptions = workflowOptions.Value;
        _notifier = notifier;
        _replyCoordinator = replyCoordinator;
        _logger = logger;
    }

    public async Task<TakeoverOperationResult> TakeOverAsync(
        Guid conversationId,
        TakeoverChangeSource source,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return new TakeoverOperationResult(false, false, BotMode.Bot, BotMode.Bot, ConversationStage.Start, ConversationStatus.Active);
        }

        var previous = conversation.BotMode;
        if (previous == BotMode.Human &&
            conversation.Status == ConversationStatus.InHumanSupport &&
            conversation.CurrentStage == ConversationStage.HumanHandoff)
        {
            return Result(conversation, previous, changed: false);
        }

        conversation.BotMode = BotMode.Human;
        conversation.Status = ConversationStatus.InHumanSupport;
        conversation.CurrentStage = ConversationStage.HumanHandoff;

        _replyCoordinator?.CancelPendingReply(conversationId);

        if (previous != BotMode.Human)
        {
            await WriteAuditAsync(conversation.Id, previous, BotMode.Human, source, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);
        _logger.LogInformation(
            "Conversation {ConversationId} takeover applied source={Source}",
            conversationId,
            source);

        return Result(conversation, previous, changed: previous != BotMode.Human);
    }

    public async Task<TakeoverOperationResult> PauseAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return new TakeoverOperationResult(false, false, BotMode.Bot, BotMode.Bot, ConversationStage.Start, ConversationStatus.Active);
        }

        var previous = conversation.BotMode;
        if (previous == BotMode.Paused)
        {
            return Result(conversation, previous, changed: false);
        }

        conversation.BotMode = BotMode.Paused;
        _replyCoordinator?.CancelPendingReply(conversationId);
        await WriteAuditAsync(conversation.Id, previous, BotMode.Paused, TakeoverChangeSource.AdminPause, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);

        _logger.LogInformation("Conversation {ConversationId} paused", conversationId);
        return Result(conversation, previous, changed: true);
    }

    public async Task<TakeoverOperationResult> ResumeBotAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return new TakeoverOperationResult(false, false, BotMode.Bot, BotMode.Bot, ConversationStage.Start, ConversationStatus.Active);
        }

        var previous = conversation.BotMode;
        if (previous == BotMode.Bot)
        {
            return Result(conversation, previous, changed: false);
        }

        var effectiveStage = await ResolveEffectiveStageAsync(conversation, cancellationToken);
        conversation.BotMode = BotMode.Bot;
        conversation.CurrentStage = effectiveStage;

        if (conversation.Status == ConversationStatus.InHumanSupport &&
            effectiveStage != ConversationStage.HumanHandoff)
        {
            conversation.Status = ConversationStatus.Active;
        }

        await WriteAuditAsync(conversation.Id, previous, BotMode.Bot, TakeoverChangeSource.AdminResume, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);

        _logger.LogInformation(
            "Conversation {ConversationId} resumed to Bot at stage {Stage}",
            conversationId,
            effectiveStage);

        return Result(conversation, previous, changed: true);
    }

    private async Task<ConversationStage> ResolveEffectiveStageAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        var supportCase = await _db.TechnicalSupportCases
            .AsNoTracking()
            .Include(c => c.ScreenInformation)
            .Include(c => c.ReceiverInformation)
            .Include(c => c.ConfigConnection)
            .Include(c => c.AnyDeskInformation)
            .Include(c => c.PanelLabelPhotos)
            .FirstOrDefaultAsync(c => c.ConversationId == conversation.Id, cancellationToken);

        var stageConversation = new Conversation
        {
            BotMode = BotMode.Bot,
            CurrentStage = ConversationStage.Start,
        };

        var slots = WorkflowSlotState.FromCase(supportCase, stageConversation);
        var assetAvailable = !string.IsNullOrWhiteSpace(_workflowOptions.ConfigExampleImagePath) &&
                             File.Exists(_workflowOptions.ConfigExampleImagePath);
        var exampleCompleted = supportCase?.ConfigConnection?.ExampleImageSent == true;

        return WorkflowStageResolver.ResolveEffectiveStage(
            slots,
            assetAvailable,
            exampleCompleted || !assetAvailable);
    }

    private async Task WriteAuditAsync(
        Guid conversationId,
        BotMode previous,
        BotMode next,
        TakeoverChangeSource source,
        CancellationToken cancellationToken)
    {
        _db.BotModeChangeAudits.Add(new BotModeChangeAudit
        {
            ConversationId = conversationId,
            PreviousMode = previous,
            NewMode = next,
            Source = source,
            ChangedAt = DateTimeOffset.UtcNow,
        });

        await Task.CompletedTask;
    }

    private static TakeoverOperationResult Result(Conversation conversation, BotMode previous, bool changed) =>
        new(true, changed, previous, conversation.BotMode, conversation.CurrentStage, conversation.Status);
}
