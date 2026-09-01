using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Workflow.Ai;
using LedSupBot.Application.Workflow.Context;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Workflow;

public sealed class ConversationWorkflowService : IConversationWorkflowService
{
    private readonly IApplicationDbContext _db;
    private readonly IWorkflowOutboundDispatcher _outbound;
    private readonly AiExtractionOrchestrator _aiOrchestrator;
    private readonly AiResponsePlanner _responsePlanner;
    private readonly IConversationContextBuilder _contextBuilder;
    private readonly IAdminEventNotifier _notifier;
    private readonly AiExtractionAuditWriter _aiAudit;
    private readonly WorkflowOptions _options;
    private readonly AiExtractionOptions _aiOptions;
    private readonly ILogger<ConversationWorkflowService> _logger;

    public ConversationWorkflowService(
        IApplicationDbContext db,
        IWorkflowOutboundDispatcher outbound,
        AiExtractionOrchestrator aiOrchestrator,
        AiResponsePlanner responsePlanner,
        IConversationContextBuilder contextBuilder,
        IAdminEventNotifier notifier,
        AiExtractionAuditWriter aiAudit,
        IOptions<WorkflowOptions> options,
        IOptions<AiExtractionOptions> aiOptions,
        ILogger<ConversationWorkflowService> logger)
    {
        _db = db;
        _outbound = outbound;
        _aiOrchestrator = aiOrchestrator;
        _responsePlanner = responsePlanner;
        _contextBuilder = contextBuilder;
        _notifier = notifier;
        _aiAudit = aiAudit;
        _options = options.Value;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    public async Task ProcessCustomerInboundAsync(WorkflowInboundMessage inbound, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Workflow processing inbound conversationId={ConversationId} externalMessageId={ExternalMessageId} messageType={MessageType}",
            inbound.ConversationId,
            inbound.ExternalMessageId,
            inbound.MessageType);

        var conversation = await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == inbound.ConversationId, cancellationToken);

        if (conversation is null || !ConversationBotPolicy.CanAutoReply(conversation.BotMode))
        {
            _logger.LogInformation(
                "Workflow skipped; conversation missing or BotMode={BotMode}",
                conversation?.BotMode.ToString() ?? "missing");
            return;
        }

        var supportCase = await LoadOrCreateCaseAsync(conversation.Id, cancellationToken);
        var outboundTexts = new List<string>();
        var handoffPending = false;
        string? configExampleImagePath = null;
        var stage = conversation.CurrentStage;

        if (stage == ConversationStage.Start)
        {
            outboundTexts.Add(WorkflowPrompts.Greeting);
            stage = ConversationStage.AskScreenSize;
        }

        await ApplyInboundContentAsync(conversation.Id, supportCase, inbound, outboundTexts, cancellationToken);

        if (!await IsAutoReplyAllowedAsync(conversation.Id, cancellationToken))
        {
            _logger.LogInformation(
                "Workflow halted before outbound; conversation {ConversationId} is no longer in Bot mode",
                conversation.Id);
            return;
        }

        // Reload slot graph after mutations.
        supportCase = await LoadCaseGraphAsync(supportCase.Id, cancellationToken);
        var slots = WorkflowSlotState.FromCase(supportCase, conversation);
        var assetAvailable = ConfigExampleAssetAvailable();
        var exampleCompleted = supportCase.ConfigConnection?.ExampleImageSent == true;
        var effectiveStage = WorkflowStageResolver.ResolveEffectiveStage(slots, assetAvailable, exampleCompleted || !assetAvailable);

        var context = await _contextBuilder.BuildAsync(
            conversation,
            supportCase,
            inbound,
            slots,
            effectiveStage,
            cancellationToken);

        var aiAddedReply = false;
        if (inbound.MessageType == MessageType.Text && !string.IsNullOrWhiteSpace(inbound.TextContent))
        {
            var plan = await _responsePlanner.TryPlanAsync(context, inbound.TextContent, cancellationToken);
            if (plan?.IssueSummary is not null)
            {
                await UpdateIssueSummaryAsync(supportCase.Id, plan.IssueSummary, cancellationToken);
                supportCase.IssueSummary = plan.IssueSummary;
            }

            if (plan?.HasUsableReply == true)
            {
                outboundTexts.Add(plan.Reply!);
                aiAddedReply = true;
            }
        }

        if (AnyDeskReadinessDetector.IsReadyForHandoff(slots, effectiveStage))
        {
            await TryEmitAnyDeskAlertAsync(conversation, supportCase, slots, cancellationToken);
        }

        if (!aiAddedReply &&
            !outboundTexts.Any(t => t.Contains("teyit", StringComparison.OrdinalIgnoreCase) ||
                                    t.Contains("Yatay", StringComparison.OrdinalIgnoreCase) ||
                                    t.Contains("Dikey", StringComparison.OrdinalIgnoreCase) ||
                                    t.Contains("Örnek:", StringComparison.OrdinalIgnoreCase)))
        {
            AppendStagePrompt(effectiveStage, inbound, assetAvailable, exampleCompleted, outboundTexts, ref configExampleImagePath);
            if (effectiveStage == ConversationStage.SendConfigExample && !exampleCompleted)
            {
                await MarkExampleSentAsync(supportCase.Id, cancellationToken);
                exampleCompleted = true;
                effectiveStage = WorkflowStageResolver.ResolveEffectiveStage(
                    slots,
                    assetAvailable,
                    exampleCompleted || !assetAvailable);
                if (effectiveStage == ConversationStage.AskAnyDesk)
                {
                    outboundTexts.Add(WorkflowPrompts.AskAnyDesk);
                }
            }
        }

        if (effectiveStage == ConversationStage.CollectingFinalInfo &&
            slots.IsScreenComplete &&
            slots.HasPanelLabelPhoto &&
            slots.ReceiverCount is > 0 &&
            slots.ConfigConfirmed &&
            slots.HasAnyDesk)
        {
            var summary = WorkflowPrompts.BuildSummary(
                slots.WidthCm!.Value,
                slots.HeightCm!.Value,
                slots.ReceiverCount!.Value,
                slots.ConfigConfirmed,
                slots.HasPanelLabelPhoto);

            await _db.TechnicalSupportCases
                .Where(c => c.Id == supportCase.Id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.SummaryMarkdown, summary)
                        .SetProperty(c => c.Status, SupportCaseStatus.ReadyForTechnician),
                    cancellationToken);

            outboundTexts.Add(summary);
            handoffPending = true;
            effectiveStage = ConversationStage.CollectingFinalInfo;
        }

        await _db.Conversations
            .Where(c => c.Id == conversation.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.CurrentStage, effectiveStage),
                cancellationToken);

        if (outboundTexts.Count > 0)
        {
            _logger.LogInformation(
                "Workflow dispatching {Count} outbound text(s) for conversation {ConversationId}",
                outboundTexts.Count,
                conversation.Id);
            await _outbound.SendTextsAsync(conversation.Id, inbound.CustomerPhoneE164, outboundTexts, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Workflow produced no outbound texts for conversation {ConversationId} stage={Stage}",
                conversation.Id,
                effectiveStage);
        }

        if (!string.IsNullOrWhiteSpace(configExampleImagePath))
        {
            await _outbound.TrySendConfigExampleImageAsync(
                conversation.Id,
                inbound.CustomerPhoneE164,
                configExampleImagePath,
                WorkflowPrompts.ConfigExampleSent,
                cancellationToken);
        }

        if (handoffPending)
        {
            await _db.Conversations
                .Where(c => c.Id == conversation.Id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.BotMode, BotMode.Human)
                        .SetProperty(c => c.Status, ConversationStatus.InHumanSupport)
                        .SetProperty(c => c.CurrentStage, ConversationStage.HumanHandoff),
                    cancellationToken);
        }
    }

    private async Task UpdateIssueSummaryAsync(Guid caseId, string issueSummary, CancellationToken cancellationToken)
    {
        await _db.TechnicalSupportCases
            .Where(c => c.Id == caseId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.IssueSummary, issueSummary),
                cancellationToken);
    }

    private async Task TryEmitAnyDeskAlertAsync(
        Conversation conversation,
        TechnicalSupportCase supportCase,
        WorkflowSlotState slots,
        CancellationToken cancellationToken)
    {
        if (supportCase.AnyDeskAlertSentAt is not null)
        {
            return;
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversation.CustomerId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await _db.TechnicalSupportCases
            .Where(c => c.Id == supportCase.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.AnyDeskAlertSentAt, now),
                cancellationToken);

        supportCase.AnyDeskAlertSentAt = now;

        var receiver = await _db.ReceiverInformations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TechnicalSupportCaseId == supportCase.Id, cancellationToken);

        var receiverSummary = receiver is null || receiver.Count <= 0
            ? null
            : string.Join(" ", new[] { receiver.Manufacturer, receiver.Model, $"×{receiver.Count}" }.Where(s => !string.IsNullOrWhiteSpace(s)));

        await _notifier.NotifyAnyDeskReadyForHandoffAsync(
            new AnyDeskReadyNotification(
                conversation.Id,
                AdminDisplayFormat.FormatCustomerIdentifier(customer ?? new Customer { PhoneNumber = "?" }),
                supportCase.IssueSummary,
                slots.IsScreenComplete ? $"{slots.WidthCm:0}×{slots.HeightCm:0} cm" : null,
                receiverSummary,
                slots.HasPanelLabelPhoto ? "alındı" : "bekleniyor",
                slots.ConfigConfirmed ? "tamam" : "bekleniyor",
                "bekleniyor"),
            cancellationToken);
    }

    private async Task ApplyInboundContentAsync(
        Guid conversationId,
        TechnicalSupportCase supportCase,
        WorkflowInboundMessage inbound,
        List<string> outboundTexts,
        CancellationToken cancellationToken)
    {
        if (inbound.MessageType == MessageType.Image)
        {
            // Panel label photos are persisted by the inbound media pipeline before workflow runs.
            return;
        }

        if (string.IsNullOrWhiteSpace(inbound.TextContent))
        {
            return;
        }

        var screen = await _db.ScreenInformations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TechnicalSupportCaseId == supportCase.Id, cancellationToken);
        var receiver = await _db.ReceiverInformations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TechnicalSupportCaseId == supportCase.Id, cancellationToken);
        var slots = new WorkflowSlotState
        {
            WidthCm = screen is { Width: > 0 } ? screen.Width : null,
            HeightCm = screen is { Height: > 0 } ? screen.Height : null,
            ReceiverCount = receiver?.Count,
            ConfigConfirmed = await _db.ConfigConnections.AsNoTracking()
                .AnyAsync(c => c.TechnicalSupportCaseId == supportCase.Id && c.Status == ConfigConnectionStatus.Confirmed, cancellationToken),
            HasAnyDesk = await _db.AnyDeskInformations.AsNoTracking()
                .AnyAsync(a => a.TechnicalSupportCaseId == supportCase.Id, cancellationToken),
        };

        var deterministic = DeterministicExtractionResult.Analyze(inbound.TextContent);

        await ApplyScreenSlotsAsync(supportCase.Id, inbound.TextContent, slots, outboundTexts, cancellationToken);
        await ApplyReceiverSlotAsync(supportCase.Id, inbound.TextContent, cancellationToken);
        await ApplyConfigSlotAsync(supportCase.Id, inbound.TextContent, slots, outboundTexts, cancellationToken);
        await ApplyAnyDeskSlotAsync(supportCase.Id, inbound.TextContent, outboundTexts, cancellationToken);

        var aiValidated = await _aiOrchestrator.TryExtractAsync(
            inbound.TextContent,
            BuildKnownSlots(slots),
            deterministic,
            cancellationToken);

        if (aiValidated is not null)
        {
            if (!await IsAutoReplyAllowedAsync(conversationId, cancellationToken))
            {
                _logger.LogInformation(
                    "Discarding in-flight AI extraction for conversation {ConversationId}; BotMode changed",
                    conversationId);
                return;
            }

            await ApplyAiValidatedSlotsAsync(supportCase.Id, slots, aiValidated, outboundTexts, cancellationToken);
            foreach (var prompt in aiValidated.ClarificationPrompts)
            {
                if (!outboundTexts.Contains(prompt))
                {
                    outboundTexts.Add(prompt);
                }
            }

            try
            {
                await _aiAudit.WriteAsync(
                    conversationId,
                    inbound.MessageId,
                    aiValidated,
                    _aiOptions.ModelName,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist AI extraction audit metadata");
            }
        }
    }

    private static Dictionary<string, string?> BuildKnownSlots(WorkflowSlotState slots) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["widthCm"] = slots.WidthCm?.ToString("0"),
            ["heightCm"] = slots.HeightCm?.ToString("0"),
            ["receiverCount"] = slots.ReceiverCount?.ToString(),
            ["configConfirmed"] = slots.ConfigConfirmed ? "true" : null,
            ["anyDesk"] = slots.HasAnyDesk ? "present" : null,
        };

    private async Task ApplyAiValidatedSlotsAsync(
        Guid caseId,
        WorkflowSlotState slotsBeforeAi,
        ValidatedAiExtraction ai,
        List<string> outboundTexts,
        CancellationToken cancellationToken)
    {
        if (ai.Screen is { Complete: true } screen && !slotsBeforeAi.IsScreenComplete)
        {
            await UpsertScreenAsync(caseId, screen.WidthCm, screen.HeightCm, "ai-validated", cancellationToken);
        }
        else if (ai.Screen is { Complete: false } partial)
        {
            if (partial.WidthCm > 0 && slotsBeforeAi.WidthCm is null or <= 0)
            {
                await UpsertScreenAsync(caseId, partial.WidthCm, partial.HeightCm, "ai-validated-partial", cancellationToken);
            }
            else if (partial.HeightCm > 0 && slotsBeforeAi.HeightCm is null or <= 0)
            {
                await UpsertScreenAsync(caseId, partial.WidthCm, partial.HeightCm, "ai-validated-partial", cancellationToken);
            }
        }

        if (ai.ReceiverCount is > 0 && slotsBeforeAi.ReceiverCount is null or <= 0)
        {
            var existing = await _db.ReceiverInformations
                .FirstOrDefaultAsync(r => r.TechnicalSupportCaseId == caseId, cancellationToken);
            if (existing is null)
            {
                _db.ReceiverInformations.Add(new ReceiverInformation
                {
                    TechnicalSupportCaseId = caseId,
                    Count = ai.ReceiverCount.Value,
                    RawUtterance = "ai-validated",
                });
            }
            else
            {
                existing.Count = ai.ReceiverCount.Value;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        if (ai.ConfigConfirmed == true && !slotsBeforeAi.ConfigConfirmed)
        {
            await UpsertConfigAsync(caseId, ConfigConnectionStatus.Confirmed, exampleSent: null, cancellationToken);
        }
        else if (ai.ConfigConfirmed == false && !slotsBeforeAi.ConfigConfirmed)
        {
            outboundTexts.Add(WorkflowPrompts.ExplainConfigConnection);
        }

        if (!string.IsNullOrWhiteSpace(ai.AnyDeskAddress) && !slotsBeforeAi.HasAnyDesk)
        {
            var exists = await _db.AnyDeskInformations
                .AsNoTracking()
                .AnyAsync(a => a.TechnicalSupportCaseId == caseId, cancellationToken);
            if (!exists)
            {
                _db.AnyDeskInformations.Add(new AnyDeskInformation
                {
                    TechnicalSupportCaseId = caseId,
                    AnyDeskAddress = ai.AnyDeskAddress,
                    CollectedAt = DateTimeOffset.UtcNow,
                    RawUtterance = "ai-validated",
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task ApplyScreenSlotsAsync(
        Guid caseId,
        string text,
        WorkflowSlotState slots,
        List<string> outboundTexts,
        CancellationToken cancellationToken)
    {
        var parsed = ScreenSizeParser.Parse(text);
        if (!parsed.ParsedAny)
        {
            return;
        }

        if (parsed.NeedsMeterConfirmation)
        {
            outboundTexts.Add(WorkflowPrompts.AskMeterConfirmation);
            return;
        }

        if (parsed.Ambiguous)
        {
            outboundTexts.Add(WorkflowPrompts.AskScreenClarification);
            return;
        }

        var width = parsed.WidthCm ?? slots.WidthCm;
        var height = parsed.HeightCm ?? slots.HeightCm;

        if (width is > 0 && height is > 0)
        {
            await UpsertScreenAsync(caseId, width.Value, height.Value, text, cancellationToken);
            return;
        }

        if (width is > 0 && height is null)
        {
            await UpsertScreenAsync(caseId, width.Value, 0, text, cancellationToken);
            outboundTexts.Add(WorkflowPrompts.AskMissingHeight);
            return;
        }

        if (height is > 0 && width is null)
        {
            await UpsertScreenAsync(caseId, 0, height.Value, text, cancellationToken);
            outboundTexts.Add(WorkflowPrompts.AskMissingWidth);
        }
    }

    private async Task ApplyReceiverSlotAsync(Guid caseId, string text, CancellationToken cancellationToken)
    {
        if (!ReceiverCountParser.TryParse(text, out var count))
        {
            return;
        }

        var existing = await _db.ReceiverInformations
            .FirstOrDefaultAsync(r => r.TechnicalSupportCaseId == caseId, cancellationToken);

        if (existing is null)
        {
            _db.ReceiverInformations.Add(new ReceiverInformation
            {
                TechnicalSupportCaseId = caseId,
                Count = count,
                RawUtterance = text,
            });
        }
        else
        {
            existing.Count = count;
            existing.RawUtterance = text;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyConfigSlotAsync(
        Guid caseId,
        string text,
        WorkflowSlotState slots,
        List<string> outboundTexts,
        CancellationToken cancellationToken)
    {
        if (slots.ConfigConfirmed)
        {
            return;
        }

        var result = ConfigConfirmationParser.Parse(text);
        if (result == ConfigConfirmationResult.Confirmed)
        {
            await UpsertConfigAsync(caseId, ConfigConnectionStatus.Confirmed, exampleSent: null, cancellationToken);
            return;
        }

        if (result == ConfigConfirmationResult.Negative)
        {
            outboundTexts.Add(WorkflowPrompts.ExplainConfigConnection);
        }
    }

    private async Task ApplyAnyDeskSlotAsync(
        Guid caseId,
        string text,
        List<string> outboundTexts,
        CancellationToken cancellationToken)
    {
        var exists = await _db.AnyDeskInformations
            .AsNoTracking()
            .AnyAsync(a => a.TechnicalSupportCaseId == caseId, cancellationToken);
        if (exists)
        {
            return;
        }

        if (AnyDeskParser.TryParse(text, out var address))
        {
            _db.AnyDeskInformations.Add(new AnyDeskInformation
            {
                TechnicalSupportCaseId = caseId,
                AnyDeskAddress = address,
                CollectedAt = DateTimeOffset.UtcNow,
                RawUtterance = text,
            });
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (AnyDeskParser.LooksLikeAttempt(text))
        {
            outboundTexts.Add(WorkflowPrompts.AskAnyDeskClarification);
        }
    }

    private void AppendStagePrompt(
        ConversationStage effectiveStage,
        WorkflowInboundMessage inbound,
        bool assetAvailable,
        bool exampleCompleted,
        List<string> outboundTexts,
        ref string? configExampleImagePath)
    {
        switch (effectiveStage)
        {
            case ConversationStage.AskScreenSize:
                outboundTexts.Add(WorkflowPrompts.AskScreenSize);
                break;

            case ConversationStage.AskPanelLabelPhotos:
                outboundTexts.Add(inbound.MessageType == MessageType.Text
                    ? WorkflowPrompts.RemindPanelLabelPhotos
                    : WorkflowPrompts.AskPanelLabelPhotos);
                break;

            case ConversationStage.AskReceiverCount:
                outboundTexts.Add(
                    inbound.MessageType == MessageType.Text && !string.IsNullOrWhiteSpace(inbound.TextContent)
                        ? WorkflowPrompts.AskReceiverClarification
                        : WorkflowPrompts.AskReceiverCount);
                break;

            case ConversationStage.AskConfigConnection:
                outboundTexts.Add(WorkflowPrompts.AskConfigConnection);
                break;

            case ConversationStage.SendConfigExample when !exampleCompleted:
                if (assetAvailable)
                {
                    outboundTexts.Add(WorkflowPrompts.ConfigExampleSent);
                    configExampleImagePath = _options.ConfigExampleImagePath;
                }
                else
                {
                    outboundTexts.Add(WorkflowPrompts.ConfigExampleUnavailable);
                    _logger.LogWarning("Config example asset not configured; step skipped deterministically");
                }

                break;

            case ConversationStage.AskAnyDesk:
                outboundTexts.Add(WorkflowPrompts.AskAnyDesk);
                break;
        }
    }

    private async Task<TechnicalSupportCase> LoadOrCreateCaseAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var existing = await _db.TechnicalSupportCases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var supportCase = new TechnicalSupportCase
        {
            ConversationId = conversationId,
            Status = SupportCaseStatus.CollectingInformation,
        };
        _db.TechnicalSupportCases.Add(supportCase);
        await _db.SaveChangesAsync(cancellationToken);
        return supportCase;
    }

    private async Task<TechnicalSupportCase> LoadCaseGraphAsync(Guid caseId, CancellationToken cancellationToken)
    {
        return await _db.TechnicalSupportCases
            .AsNoTracking()
            .Include(c => c.ScreenInformation)
            .Include(c => c.ReceiverInformation)
            .Include(c => c.ConfigConnection)
            .Include(c => c.AnyDeskInformation)
            .Include(c => c.PanelLabelPhotos)
            .SingleAsync(c => c.Id == caseId, cancellationToken);
    }

    private async Task<bool> IsAutoReplyAllowedAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var botMode = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.BotMode)
            .FirstOrDefaultAsync(cancellationToken);

        return ConversationBotPolicy.CanAutoReply(botMode);
    }

    private async Task UpsertScreenAsync(Guid caseId, decimal width, decimal height, string raw, CancellationToken cancellationToken)
    {
        var existing = await _db.ScreenInformations
            .FirstOrDefaultAsync(s => s.TechnicalSupportCaseId == caseId, cancellationToken);

        if (existing is null)
        {
            _db.ScreenInformations.Add(new ScreenInformation
            {
                TechnicalSupportCaseId = caseId,
                Width = width,
                Height = height,
                Unit = DimensionUnit.Cm,
                RawUtterance = raw,
            });
        }
        else
        {
            if (width > 0)
            {
                existing.Width = width;
            }

            if (height > 0)
            {
                existing.Height = height;
            }

            existing.Unit = DimensionUnit.Cm;
            existing.RawUtterance = raw;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertConfigAsync(
        Guid caseId,
        ConfigConnectionStatus status,
        bool? exampleSent,
        CancellationToken cancellationToken)
    {
        var existing = await _db.ConfigConnections
            .FirstOrDefaultAsync(c => c.TechnicalSupportCaseId == caseId, cancellationToken);

        if (existing is null)
        {
            _db.ConfigConnections.Add(new ConfigConnectionInfo
            {
                TechnicalSupportCaseId = caseId,
                Status = status,
                ExampleImageSent = exampleSent == true,
                StatusChangedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Status = status;
            existing.StatusChangedAt = DateTimeOffset.UtcNow;
            if (exampleSent is not null)
            {
                existing.ExampleImageSent = exampleSent.Value;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkExampleSentAsync(Guid caseId, CancellationToken cancellationToken)
    {
        var existing = await _db.ConfigConnections
            .FirstOrDefaultAsync(c => c.TechnicalSupportCaseId == caseId, cancellationToken);

        if (existing is null)
        {
            _db.ConfigConnections.Add(new ConfigConnectionInfo
            {
                TechnicalSupportCaseId = caseId,
                Status = ConfigConnectionStatus.Confirmed,
                ExampleImageSent = true,
                StatusChangedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.ExampleImageSent = true;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private bool ConfigExampleAssetAvailable() =>
        !string.IsNullOrWhiteSpace(_options.ConfigExampleImagePath) &&
        File.Exists(_options.ConfigExampleImagePath);
}
