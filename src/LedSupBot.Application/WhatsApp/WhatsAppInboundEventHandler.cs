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
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.WhatsApp;

public sealed class WhatsAppInboundEventHandler : IWhatsAppInboundEventHandler
{
    private readonly IServiceProvider _services;
    private readonly IWhatsAppConnectionStateStore _stateStore;
    private readonly IWhatsAppSelfIdentityStore _selfIdentity;
    private readonly IOptions<WhatsAppTestModeOptions> _testModeOptions;
    private readonly ILogger<WhatsAppInboundEventHandler> _logger;

    public WhatsAppInboundEventHandler(
        IServiceProvider services,
        IWhatsAppConnectionStateStore stateStore,
        IWhatsAppSelfIdentityStore selfIdentity,
        IOptions<WhatsAppTestModeOptions> testModeOptions,
        ILogger<WhatsAppInboundEventHandler> logger)
    {
        _services = services;
        _stateStore = stateStore;
        _selfIdentity = selfIdentity;
        _testModeOptions = testModeOptions;
        _logger = logger;
    }

    public async Task HandleAsync(WhatsAppInboundEvent inboundEvent, CancellationToken cancellationToken = default)
    {
        if (string.Equals(inboundEvent.EventType, "ConnectionStateChanged", StringComparison.OrdinalIgnoreCase))
        {
            var mappedStatus = WhatsAppEventMapper.MapConnectionStatus(inboundEvent.Status);
            _stateStore.Set(mappedStatus);
            _logger.LogInformation("WhatsApp connection state updated to {Status}", _stateStore.Current);
            var notifier = _services.GetService<IAdminEventNotifier>();
            if (notifier is not null)
            {
                await notifier.NotifyWhatsAppStatusChangedAsync(_stateStore.Current.ToString(), cancellationToken);
            }
            return;
        }

        if (string.Equals(inboundEvent.EventType, "LidPhoneMapping", StringComparison.OrdinalIgnoreCase))
        {
            await PersistLidPhoneMappingAsync(inboundEvent, cancellationToken);
            return;
        }

        if (string.Equals(inboundEvent.EventType, "ConnectedIdentity", StringComparison.OrdinalIgnoreCase))
        {
            _selfIdentity.SetSelfJids([inboundEvent.SelfPnJid, inboundEvent.SelfLidJid]);
            await ScrubPoisonedSelfRoutesAsync(cancellationToken);
            _logger.LogInformation(
                "Connected business identity registered selfJidCount={Count}",
                _selfIdentity.SelfJids.Count);
            return;
        }

        if (string.Equals(inboundEvent.EventType, "MessageDeliveryUpdate", StringComparison.OrdinalIgnoreCase))
        {
            await HandleMessageDeliveryUpdateAsync(inboundEvent, cancellationToken);
            return;
        }

        if (!string.Equals(inboundEvent.EventType, "IncomingMessage", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Ignoring unsupported WhatsApp event type");
            return;
        }

        if (WhatsAppEventMapper.IsGroupJid(inboundEvent.RemoteJid))
        {
            _logger.LogInformation("WhatsApp inbound received type=group; zero bot automation");
            return;
        }

        _logger.LogInformation(
            "WhatsApp inbound received type=private messageType={MessageType} fromMe={FromMe}",
            inboundEvent.MessageType,
            inboundEvent.FromMe);

        // Defense in depth: even a direct/replayed webhook must not put a
        // local authenticated-account message into customer automation.
        if (inboundEvent.FromMe)
        {
            _logger.LogInformation("fromMe WhatsApp event ignored; no customer automation");
            return;
        }

        if (_selfIdentity.SelfJids.Count == 0)
        {
            var identitySync = _services.GetService<IWhatsAppSelfIdentitySync>();
            if (identitySync is not null)
            {
                await identitySync.SyncFromConnectorAsync(cancellationToken);
            }
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

        var phone = WhatsAppEventMapper.NormalizeWhatsAppAddress(customerAddress);
        var customer = await ResolveCustomerAsync(db, phone, inboundEvent, cancellationToken);

        var conversation = await ResolveActiveConversationAsync(db, customer, cancellationToken);

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
            if (sender == MessageSender.Customer && !string.IsNullOrWhiteSpace(inboundEvent.RemoteJid))
            {
                var normalizedRoute = WhatsAppEventMapper.NormalizeWhatsAppAddress(inboundEvent.RemoteJid);
                if (_selfIdentity.IsSelfJid(normalizedRoute))
                {
                    _logger.LogWarning("Inbound route ignored: self JID must not poison customer route");
                }
                else
                {
                    conversation.WhatsAppRouteJid = normalizedRoute;
                    _logger.LogInformation(
                        "Inbound route captured routeType={RouteType} selfMatch=false",
                        WhatsAppEventMapper.IsLidJid(normalizedRoute) ? "lid" : "other");
                }
            }
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
                        inboundEvent,
                        customer,
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
            inboundEvent,
            await LoadCustomerForConversationAsync(conversationId, cancellationToken),
            inboundEvent.TextContent,
            cancellationToken);
    }

    private async Task<Customer?> LoadCustomerForConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var db = _services.GetRequiredService<IApplicationDbContext>();
        return await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.Customer)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task InvokeWorkflowIfAllowedAsync(
        Guid conversationId,
        string phone,
        Message message,
        WhatsAppInboundEvent inboundEvent,
        Customer? customer,
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
            _logger.LogInformation(
                "Workflow skipped conversationId={ConversationId} botMode={BotMode}",
                conversationId,
                botMode);
            return;
        }

        var testAllowlist = _services.GetRequiredService<IWhatsAppTestAllowlist>();
        if (!testAllowlist.IsAutoReplyAllowed(inboundEvent, phone, customer))
        {
            return;
        }

        _logger.LogInformation(
            "Workflow resolve conversationId={ConversationId} botMode={BotMode} workflowResolved={Resolved}",
            conversationId,
            botMode,
            true);

        var customerIdentity = customer?.PhoneNumber ?? phone;
        var deliveryRoute = ResolveWhatsAppDeliveryRoute(inboundEvent, _selfIdentity);

        var coordinator = _services.GetService<IConversationReplyCoordinator>();
        if (coordinator is not null)
        {
            coordinator.EnqueueCustomerInbound(new PendingCustomerInbound(
                conversationId,
                customerIdentity,
                message.Id,
                message.ExternalMessageId!,
                message.MessageType,
                textContent,
                deliveryRoute));
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
                customerIdentity,
                message.Id,
                message.ExternalMessageId!,
                message.MessageType,
                textContent,
                WhatsAppDeliveryRoute: deliveryRoute),
            cancellationToken);
    }

    private static string? ResolveWhatsAppDeliveryRoute(
        WhatsAppInboundEvent inboundEvent,
        IWhatsAppSelfIdentityStore selfIdentity)
    {
        if (string.IsNullOrWhiteSpace(inboundEvent.RemoteJid) ||
            WhatsAppEventMapper.IsGroupJid(inboundEvent.RemoteJid))
        {
            return null;
        }

        var normalized = WhatsAppEventMapper.NormalizeWhatsAppAddress(inboundEvent.RemoteJid);
        return selfIdentity.IsSelfJid(normalized) ? null : normalized;
    }

    private async Task ScrubPoisonedSelfRoutesAsync(CancellationToken cancellationToken)
    {
        if (_selfIdentity.SelfJids.Count == 0)
        {
            return;
        }

        var db = _services.GetService<IApplicationDbContext>();
        if (db is null)
        {
            return;
        }

        var customers = await db.Customers.ToListAsync(cancellationToken);
        foreach (var customer in customers)
        {
            if (!string.IsNullOrWhiteSpace(customer.LinkedWhatsAppLid) &&
                _selfIdentity.IsSelfJid(customer.LinkedWhatsAppLid))
            {
                customer.LinkedWhatsAppLid = null;
            }
        }

        var conversations = await db.Conversations
            .Where(c => c.LastInboundWhatsAppAddress != null)
            .ToListAsync(cancellationToken);
        foreach (var conversation in conversations)
        {
            if (_selfIdentity.IsSelfJid(conversation.LastInboundWhatsAppAddress))
            {
                conversation.LastInboundWhatsAppAddress = null;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleMessageDeliveryUpdateAsync(
        WhatsAppInboundEvent inboundEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inboundEvent.ExternalMessageId) ||
            string.IsNullOrWhiteSpace(inboundEvent.DeliveryStatus))
        {
            return;
        }

        var mapped = MapConnectorDeliveryStatus(inboundEvent.DeliveryStatus);
        if (mapped is null)
        {
            return;
        }

        var db = _services.GetService<IApplicationDbContext>();
        if (db is null)
        {
            return;
        }

        var message = await db.Messages
            .FirstOrDefaultAsync(
                m => m.ExternalMessageId == inboundEvent.ExternalMessageId && m.SentBy == MessageSender.Bot,
                cancellationToken);
        if (message is null)
        {
            return;
        }

        if (!ShouldAdvanceDeliveryStatus(message.DeliveryStatus, mapped.Value))
        {
            return;
        }

        message.DeliveryStatus = mapped.Value;
        await db.SaveChangesAsync(cancellationToken);

        var notifier = _services.GetService<IAdminEventNotifier>();
        if (notifier is not null)
        {
            await notifier.NotifyMessageReceivedAsync(message.ConversationId, message.Id, cancellationToken);
            await notifier.NotifyConversationUpdatedAsync(message.ConversationId, cancellationToken);
        }

        _logger.LogInformation(
            "Bot outbound delivery ack externalMessageIdPresent=true deliveryStatus={DeliveryStatus}",
            mapped.Value);
    }

    private static DeliveryStatus? MapConnectorDeliveryStatus(string? status) =>
        status?.ToUpperInvariant() switch
        {
            "SENT" => DeliveryStatus.Sent,
            "DELIVERED" => DeliveryStatus.Delivered,
            "READ" => DeliveryStatus.Read,
            "FAILED" => DeliveryStatus.Failed,
            _ => null
        };

    private static bool ShouldAdvanceDeliveryStatus(DeliveryStatus current, DeliveryStatus next) =>
        next switch
        {
            DeliveryStatus.Failed => current is DeliveryStatus.Pending or DeliveryStatus.Sent,
            DeliveryStatus.Sent => current == DeliveryStatus.Pending,
            DeliveryStatus.Delivered => current is DeliveryStatus.Pending or DeliveryStatus.Sent,
            DeliveryStatus.Read => current is DeliveryStatus.Pending or DeliveryStatus.Sent or DeliveryStatus.Delivered,
            _ => false
        };

    private async Task TryInvokeWorkflowForPersistedCustomerInboundAsync(
        WhatsAppInboundEvent inboundEvent,
        Message? knownMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inboundEvent.ExternalMessageId) || inboundEvent.FromMe)
        {
            return;
        }

        var customerAddress = WhatsAppEventMapper.ResolveCustomerAddress(inboundEvent);
        if (string.IsNullOrWhiteSpace(customerAddress))
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

        var phone = WhatsAppEventMapper.NormalizeWhatsAppAddress(customerAddress);
        var customer = await ResolveCustomerAsync(db, phone, inboundEvent, cancellationToken);
        var conversation = await ResolveActiveConversationAsync(db, customer, cancellationToken);
        if (conversation is null)
        {
            return;
        }

        await InvokeWorkflowIfAllowedAsync(
            conversation.Id,
            phone,
            message,
            inboundEvent,
            customer,
            inboundEvent.TextContent,
            cancellationToken);
    }

    private async Task<Customer> ResolveCustomerAsync(
        IApplicationDbContext db,
        string phone,
        WhatsAppInboundEvent inboundEvent,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(inboundEvent.SenderPnJid))
        {
            var senderUser = inboundEvent.SenderPnJid.Split('@')[0]?.Split(':')[0];
            var canonical = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(senderUser);
            if (canonical is not null)
            {
                var linked = await FindCustomerByCanonicalPhoneAsync(db, canonical, cancellationToken);
                if (linked is not null)
                {
                    if (WhatsAppEventMapper.IsLidJid(phone))
                    {
                        await PersistTrustedLidLinkAsync(db, linked, phone, cancellationToken);
                        _logger.LogInformation("Customer LID channel linked to existing canonical identity");
                    }

                    return linked;
                }
            }
        }

        var byLinkedLid = await db.Customers
            .FirstOrDefaultAsync(c => c.LinkedWhatsAppLid == phone, cancellationToken);
        if (byLinkedLid is not null)
        {
            return byLinkedLid;
        }

        if (WhatsAppEventMapper.IsLidJid(phone))
        {
            var trustedCanonical = await TryResolveTrustedCanonicalForLidAsync(db, phone, cancellationToken);
            if (trustedCanonical is not null)
            {
                await PersistTrustedLidLinkAsync(db, trustedCanonical, phone, cancellationToken);
                _logger.LogInformation("Inbound LID mapped to canonical Bot customer via persisted trusted mapping");
                return trustedCanonical;
            }
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone, cancellationToken);
        if (customer is null)
        {
            customer = new Customer { PhoneNumber = phone };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
        }

        return customer;
    }

    private async Task<Conversation?> ResolveActiveConversationAsync(
        IApplicationDbContext db,
        Customer customer,
        CancellationToken cancellationToken)
    {
        return await db.Conversations
            .Where(c => c.CustomerId == customer.Id && c.Status != ConversationStatus.Completed && c.Status != ConversationStatus.Abandoned)
            .OrderByDescending(c => c.BotMode == BotMode.Bot)
            .ThenByDescending(c => c.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task PersistLidPhoneMappingAsync(WhatsAppInboundEvent inboundEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inboundEvent.RemoteJid) ||
            string.IsNullOrWhiteSpace(inboundEvent.SenderPnJid) ||
            !WhatsAppEventMapper.IsLidJid(inboundEvent.RemoteJid))
        {
            return;
        }

        var db = _services.GetService<IApplicationDbContext>();
        if (db is null)
        {
            return;
        }

        var lidAddress = WhatsAppEventMapper.NormalizeWhatsAppAddress(inboundEvent.RemoteJid);
        var senderUser = inboundEvent.SenderPnJid.Split('@')[0]?.Split(':')[0];
        var canonical = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(senderUser);
        if (canonical is null)
        {
            return;
        }

        var customer = await FindCustomerByCanonicalPhoneAsync(db, canonical, cancellationToken);
        if (customer is null)
        {
            return;
        }

        await PersistTrustedLidLinkAsync(db, customer, lidAddress, cancellationToken);
        _logger.LogInformation("WhatsApp LID/PN mapping persisted for trusted customer channel");
    }

    private async Task PersistTrustedLidLinkAsync(
        IApplicationDbContext db,
        Customer canonicalCustomer,
        string lidAddress,
        CancellationToken cancellationToken)
    {
        if (!WhatsAppEventMapper.IsLidJid(lidAddress) || _selfIdentity.IsSelfJid(lidAddress))
        {
            return;
        }

        var normalizedLid = WhatsAppEventMapper.NormalizeWhatsAppAddress(lidAddress);
        if (string.Equals(canonicalCustomer.LinkedWhatsAppLid, normalizedLid, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        canonicalCustomer.LinkedWhatsAppLid = normalizedLid;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Customer?> TryResolveTrustedCanonicalForLidAsync(
        IApplicationDbContext db,
        string lidAddress,
        CancellationToken cancellationToken)
    {
        var options = _testModeOptions.Value;
        if (!options.Enabled)
        {
            return null;
        }

        var allowedCanonical = PhoneNumberNormalizer.TryNormalizeToCanonicalDigits(options.AllowedPhoneNumber);
        if (allowedCanonical is null)
        {
            return null;
        }

        var canonicalCustomer = await FindCustomerByCanonicalPhoneAsync(db, allowedCanonical, cancellationToken);
        if (canonicalCustomer is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(canonicalCustomer.LinkedWhatsAppLid))
        {
            if (string.Equals(canonicalCustomer.LinkedWhatsAppLid, lidAddress, StringComparison.OrdinalIgnoreCase))
            {
                return canonicalCustomer;
            }

            _logger.LogInformation("Inbound LID differs from persisted link; re-evaluating trusted mapping");
        }

        var lidOwner = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.PhoneNumber == lidAddress, cancellationToken);
        if (lidOwner is null || lidOwner.Id == canonicalCustomer.Id)
        {
            return null;
        }

        var canonicalHasBotConversation = await db.Conversations.AsNoTracking()
            .AnyAsync(
                c => c.CustomerId == canonicalCustomer.Id &&
                     c.BotMode == BotMode.Bot &&
                     c.Status != ConversationStatus.Completed &&
                     c.Status != ConversationStatus.Abandoned,
                cancellationToken);
        if (!canonicalHasBotConversation)
        {
            return null;
        }

        var lidHasTrustedActivity = await (
            from m in db.Messages.AsNoTracking()
            join c in db.Conversations.AsNoTracking() on m.ConversationId equals c.Id
            where c.CustomerId == lidOwner.Id &&
                  m.SentBy == MessageSender.Bot &&
                  m.Content != null &&
                  m.Content.Contains("kolay gelsin")
            select m.Id).AnyAsync(cancellationToken);
        if (!lidHasTrustedActivity)
        {
            return null;
        }

        return canonicalCustomer;
    }

    private static async Task<Customer?> FindCustomerByCanonicalPhoneAsync(
        IApplicationDbContext db,
        string canonicalPhone,
        CancellationToken cancellationToken)
    {
        var localDigits = canonicalPhone.StartsWith("90", StringComparison.Ordinal) && canonicalPhone.Length == 12
            ? canonicalPhone[2..]
            : canonicalPhone;

        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            canonicalPhone,
            $"+{canonicalPhone}",
            localDigits,
            $"0{localDigits}",
            $"{localDigits}@s.whatsapp.net",
            $"{canonicalPhone}@s.whatsapp.net",
        };

        foreach (var variant in variants)
        {
            var match = await db.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == variant, cancellationToken);
            if (match is not null)
            {
                return match;
            }
        }

        var customers = await db.Customers.AsNoTracking().Select(c => new { c.Id, c.PhoneNumber }).ToListAsync(cancellationToken);
        foreach (var row in customers)
        {
            if (PhoneNumberNormalizer.CanonicalEquals(row.PhoneNumber, canonicalPhone))
            {
                return await db.Customers.FirstOrDefaultAsync(c => c.Id == row.Id, cancellationToken);
            }
        }

        return null;
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
