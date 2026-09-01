using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Media;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.WhatsApp;

public sealed class WhatsAppInboundEventHandler : IWhatsAppInboundEventHandler
{
    private readonly IServiceProvider _services;
    private readonly IWhatsAppConnectionStateStore _stateStore;
    private readonly ILogger<WhatsAppInboundEventHandler> _logger;

    public WhatsAppInboundEventHandler(
        IServiceProvider services,
        IWhatsAppConnectionStateStore stateStore,
        ILogger<WhatsAppInboundEventHandler> logger)
    {
        _services = services;
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task HandleAsync(WhatsAppInboundEvent inboundEvent, CancellationToken cancellationToken = default)
    {
        if (string.Equals(inboundEvent.EventType, "ConnectionStateChanged", StringComparison.OrdinalIgnoreCase))
        {
            _stateStore.Set(WhatsAppEventMapper.MapConnectionStatus(inboundEvent.Status));
            _logger.LogInformation("WhatsApp connection state updated to {Status}", _stateStore.Current);
            return;
        }

        if (!string.Equals(inboundEvent.EventType, "IncomingMessage", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Ignoring unsupported WhatsApp event type");
            return;
        }

        var db = _services.GetService<IApplicationDbContext>();
        if (db is null)
        {
            _logger.LogWarning("Database is not configured; inbound WhatsApp message was not persisted");
            return;
        }

        if (string.IsNullOrWhiteSpace(inboundEvent.ExternalMessageId))
        {
            _logger.LogWarning("Inbound WhatsApp message missing external id; skipped");
            return;
        }

        var existingMessage = await db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalMessageId == inboundEvent.ExternalMessageId, cancellationToken);

        if (existingMessage is not null)
        {
            if (ShouldRetryMediaProcessing(existingMessage, inboundEvent))
            {
                await RetryMediaProcessingAsync(existingMessage, inboundEvent, cancellationToken);
            }
            else if (existingMessage.SentBy == MessageSender.Customer && !inboundEvent.FromMe)
            {
                await TryInvokeWorkflowForPersistedCustomerInboundAsync(inboundEvent, existingMessage, cancellationToken);
            }

            _logger.LogDebug("Duplicate WhatsApp event ignored");
            return;
        }

        var persistedBotMessage = await db.Messages.AnyAsync(
            m => m.ExternalMessageId == inboundEvent.ExternalMessageId && m.SentBy == MessageSender.Bot,
            cancellationToken);
        var botOriginated = ConversationBotPolicy.IsBotOriginated(inboundEvent.BotOriginated, persistedBotMessage);

        if (string.IsNullOrWhiteSpace(inboundEvent.RemotePhoneNumber) &&
            string.IsNullOrWhiteSpace(inboundEvent.RemoteJid))
        {
            _logger.LogWarning("Inbound WhatsApp message missing remote phone/jid; skipped");
            return;
        }

        var customerAddress = WhatsAppEventMapper.ResolveCustomerAddress(inboundEvent);
        if (string.IsNullOrWhiteSpace(customerAddress))
        {
            _logger.LogWarning(
                "Inbound WhatsApp message has no resolvable customer address; remoteJid={RemoteJid}",
                inboundEvent.RemoteJid);
            return;
        }

        if (WhatsAppEventMapper.IsLidJid(inboundEvent.RemoteJid))
        {
            _logger.LogInformation(
                "Inbound LID identity conversationId pending remoteJidType=lid senderPnPresent={SenderPnPresent}",
                !string.IsNullOrWhiteSpace(inboundEvent.SenderPnJid));
        }

        var phone = customerAddress;
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone, cancellationToken);
        if (customer is null && WhatsAppEventMapper.IsLidJid(inboundEvent.RemoteJid))
        {
            var lidUser = inboundEvent.RemoteJid!.Split('@')[0];
            customer = await db.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == lidUser, cancellationToken);
            if (customer is not null)
            {
                customer.PhoneNumber = phone;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        if (customer is null)
        {
            customer = new Customer { PhoneNumber = phone };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
        }

        var conversation = await db.Conversations
            .Where(c => c.CustomerId == customer.Id && c.Status != ConversationStatus.Completed && c.Status != ConversationStatus.Abandoned)
            .OrderByDescending(c => c.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                CustomerId = customer.Id,
                Status = ConversationStatus.Active,
                BotMode = BotMode.Bot,
                CurrentStage = ConversationStage.Start
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);
        }

        var sender = WhatsAppEventMapper.MapSender(inboundEvent.FromMe, botOriginated);
        var direction = WhatsAppEventMapper.MapDirection(inboundEvent.FromMe);
        var messageType = WhatsAppEventMapper.MapMessageType(inboundEvent.MessageType);

        if (sender == MessageSender.Technician)
        {
            var takeover = _services.GetRequiredService<IConversationTakeoverService>();
            await takeover.TakeOverAsync(
                conversation.Id,
                TakeoverChangeSource.TechnicianMessage,
                cancellationToken);

            conversation = await db.Conversations.SingleAsync(c => c.Id == conversation.Id, cancellationToken);
        }

        if (!inboundEvent.FromMe)
        {
            conversation.LastCustomerMessageAt = inboundEvent.Timestamp ?? DateTimeOffset.UtcNow;
        }

        var message = new Message
        {
            ConversationId = conversation.Id,
            Direction = direction,
            MessageType = messageType,
            Content = inboundEvent.TextContent,
            ExternalMessageId = inboundEvent.ExternalMessageId,
            DeliveryStatus = DeliveryStatus.Delivered,
            SentBy = sender
        };

        db.Messages.Add(message);

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            var notifier = _services.GetService<IAdminEventNotifier>();
            if (notifier is not null)
            {
                await notifier.NotifyMessageReceivedAsync(conversation.Id, message.Id, cancellationToken);
                await notifier.NotifyConversationUpdatedAsync(conversation.Id, cancellationToken);
            }

            if (sender == MessageSender.Customer && !inboundEvent.FromMe)
            {
                if (messageType == MessageType.Image)
                {
                    await ProcessInboundImageAsync(
                        conversation.Id,
                        message,
                        inboundEvent,
                        phone,
                        cancellationToken);
                }
                else
                {
                    await InvokeWorkflowIfAllowedAsync(
                        conversation.Id,
                        phone,
                        message,
                        inboundEvent.TextContent,
                        cancellationToken);
                }
            }
        }
        catch (DbUpdateException)
        {
            _logger.LogDebug("Duplicate WhatsApp event caught by DB uniqueness");
            await TryInvokeWorkflowForPersistedCustomerInboundAsync(inboundEvent, cancellationToken: cancellationToken);
        }
    }

    private static bool ShouldRetryMediaProcessing(Message existingMessage, WhatsAppInboundEvent inboundEvent) =>
        existingMessage.MessageType == MessageType.Image &&
        string.IsNullOrWhiteSpace(existingMessage.StorageKey) &&
        existingMessage.SentBy == MessageSender.Customer &&
        !inboundEvent.FromMe &&
        inboundEvent.Media?.Downloadable == true;

    private async Task RetryMediaProcessingAsync(
        Message existingMessage,
        WhatsAppInboundEvent inboundEvent,
        CancellationToken cancellationToken)
    {
        var db = _services.GetRequiredService<IApplicationDbContext>();
        var conversationId = existingMessage.ConversationId;
        var tracked = await db.Messages.SingleAsync(m => m.Id == existingMessage.Id, cancellationToken);
        var phone = WhatsAppEventMapper.ResolveCustomerAddress(inboundEvent)
            ?? WhatsAppEventMapper.NormalizePhone(inboundEvent.RemotePhoneNumber!);

        await ProcessInboundImageAsync(conversationId, tracked, inboundEvent, phone, cancellationToken);
    }

    private async Task ProcessInboundImageAsync(
        Guid conversationId,
        Message message,
        WhatsAppInboundEvent inboundEvent,
        string phone,
        CancellationToken cancellationToken)
    {
        var pipeline = _services.GetService<IInboundMediaPipeline>();
        if (pipeline is not null)
        {
            var db = _services.GetRequiredService<IApplicationDbContext>();
            var supportCase = await db.TechnicalSupportCases
                .FirstOrDefaultAsync(c => c.ConversationId == conversationId, cancellationToken);

            if (supportCase is null)
            {
                supportCase = new TechnicalSupportCase
                {
                    ConversationId = conversationId,
                    Status = SupportCaseStatus.CollectingInformation,
                };
                db.TechnicalSupportCases.Add(supportCase);
                await db.SaveChangesAsync(cancellationToken);
            }

            var result = await pipeline.ProcessInboundImageAsync(
                message,
                supportCase,
                inboundEvent,
                cancellationToken);

            if (result.Outcome is MediaProcessOutcome.DownloadFailed or MediaProcessOutcome.StorageFailed)
            {
                _logger.LogWarning(
                    "Inbound media processing outcome {Outcome} for message {ExternalMessageId}",
                    result.Outcome,
                    inboundEvent.ExternalMessageId);
            }
        }

        await InvokeWorkflowIfAllowedAsync(
            conversationId,
            phone,
            message,
            inboundEvent.TextContent,
            cancellationToken);
    }

    private async Task InvokeWorkflowIfAllowedAsync(
        Guid conversationId,
        string phone,
        Message message,
        string? textContent,
        CancellationToken cancellationToken)
    {
        var db = _services.GetRequiredService<IApplicationDbContext>();
        var botMode = await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.BotMode)
            .FirstOrDefaultAsync(cancellationToken);

        if (!ConversationBotPolicy.CanAutoReply(botMode))
        {
            return;
        }

        _logger.LogInformation(
            "Workflow resolve conversationId={ConversationId} botMode={BotMode} workflowResolved={Resolved}",
            conversationId,
            botMode,
            true);

        var coordinator = _services.GetService<IConversationReplyCoordinator>();
        if (coordinator is not null)
        {
            coordinator.EnqueueCustomerInbound(new PendingCustomerInbound(
                conversationId,
                phone,
                message.Id,
                message.ExternalMessageId!,
                message.MessageType,
                textContent));
            return;
        }

        IConversationWorkflowService workflow;
        try
        {
            workflow = _services.GetRequiredService<IConversationWorkflowService>();
        }
        catch (InvalidOperationException)
        {
            _logger.LogError(
                "IConversationWorkflowService is not registered; auto-reply skipped for conversation {ConversationId}. Ensure Database:ConnectionString is configured.",
                conversationId);
            return;
        }

        await workflow.ProcessCustomerInboundAsync(
            new WorkflowInboundMessage(
                conversationId,
                phone,
                message.Id,
                message.ExternalMessageId!,
                message.MessageType,
                textContent),
            cancellationToken);
    }

    private async Task TryInvokeWorkflowForPersistedCustomerInboundAsync(
        WhatsAppInboundEvent inboundEvent,
        Message? knownMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inboundEvent.ExternalMessageId) ||
            string.IsNullOrWhiteSpace(inboundEvent.RemotePhoneNumber) ||
            inboundEvent.FromMe)
        {
            return;
        }

        var db = _services.GetService<IApplicationDbContext>();
        if (db is null)
        {
            return;
        }

        var message = knownMessage ?? await db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalMessageId == inboundEvent.ExternalMessageId, cancellationToken);

        if (message is null || message.SentBy != MessageSender.Customer)
        {
            return;
        }

        if (await HasBotOutboundAfterAsync(message, cancellationToken))
        {
            return;
        }

        var phone = WhatsAppEventMapper.ResolveCustomerAddress(inboundEvent)
            ?? WhatsAppEventMapper.NormalizePhone(inboundEvent.RemotePhoneNumber!);
        await InvokeWorkflowIfAllowedAsync(
            message.ConversationId,
            phone,
            message,
            inboundEvent.TextContent,
            cancellationToken);
    }

    private async Task<bool> HasBotOutboundAfterAsync(Message customerMessage, CancellationToken cancellationToken)
    {
        var db = _services.GetRequiredService<IApplicationDbContext>();
        return await db.Messages.AnyAsync(
            m => m.ConversationId == customerMessage.ConversationId &&
                 m.SentBy == MessageSender.Bot &&
                 m.Direction == MessageDirection.Outbound &&
                 m.CreatedAt >= customerMessage.CreatedAt,
            cancellationToken);
    }
}
