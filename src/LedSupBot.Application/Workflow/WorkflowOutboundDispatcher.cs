using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Workflow;

public interface IWorkflowOutboundDispatcher
{
    Task SendTextsAsync(
        Guid conversationId,
        string customerIdentity,
        string? whatsAppDeliveryRoute,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    Task TrySendConfigExampleImageAsync(
        Guid conversationId,
        string customerIdentity,
        string? whatsAppDeliveryRoute,
        string imagePath,
        string? caption,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowOutboundDispatcher : IWorkflowOutboundDispatcher
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WorkflowOutboundDispatcher> _logger;

    public WorkflowOutboundDispatcher(IServiceProvider services, ILogger<WorkflowOutboundDispatcher> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task SendTextsAsync(
        Guid conversationId,
        string customerIdentity,
        string? whatsAppDeliveryRoute,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return;
        }

        if (WhatsAppEventMapper.IsGroupJid(whatsAppDeliveryRoute ?? customerIdentity))
        {
            _logger.LogWarning("Bot auto-reply skipped for WhatsApp group");
            return;
        }

        var db = _services.GetRequiredService<IApplicationDbContext>();
        var options = _services.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        var coordinator = _services.GetService<IConversationReplyCoordinator>();
        var linkedToken = coordinator?.LinkReplyCancellation(conversationId, cancellationToken) ?? cancellationToken;

        if (!options.AutoReplyEnabled)
        {
            _logger.LogWarning(
                "Workflow auto-reply disabled (Workflow:AutoReplyEnabled=false); skipped {Count} outbound message(s) for conversation {ConversationId}",
                texts.Count,
                conversationId);
            return;
        }

        var testAllowlist = _services.GetRequiredService<IWhatsAppTestAllowlist>();
        var selfIdentity = _services.GetRequiredService<IWhatsAppSelfIdentityStore>();
        if (selfIdentity.SelfJids.Count == 0)
        {
            var identitySync = _services.GetService<IWhatsAppSelfIdentitySync>();
            if (identitySync is not null)
            {
                await identitySync.SyncFromConnectorAsync(cancellationToken);
            }
        }

        Customer? customer = null;
        var customerId = await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => (Guid?)c.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (customerId is not null)
        {
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        }

        var allowlistAddress = whatsAppDeliveryRoute ?? customerIdentity;
        if (!testAllowlist.IsOutboundAllowed(allowlistAddress, customer))
        {
            _logger.LogInformation("Test allowlist blocked outbound conversationId={ConversationId}", conversationId);
            return;
        }

        var destination = await WhatsAppOutboundDestinationResolver.ResolveAsync(
            db,
            selfIdentity,
            conversationId,
            whatsAppDeliveryRoute,
            customerIdentity,
            cancellationToken);

        if (!testAllowlist.IsOutboundAllowed(destination.Address, customer))
        {
            _logger.LogInformation("Test allowlist / exclusion blocked resolved destination conversationId={ConversationId}", conversationId);
            return;
        }

        if (destination.SelfMatch || selfIdentity.IsSelfJid(destination.Address))
        {
            _logger.LogWarning(
                "Outbound blocked: self destination conversationId={ConversationId} destinationType={DestinationType}",
                conversationId,
                destination.DestinationType);
            return;
        }

        if (WhatsAppEventMapper.IsGroupJid(destination.Address))
        {
            _logger.LogWarning("Bot auto-reply skipped for resolved WhatsApp group destination");
            return;
        }

        var client = _services.GetRequiredService<IWhatsAppClient>();
        var recorder = _services.GetRequiredService<IWhatsAppOutboundRecorder>();

        _logger.LogInformation(
            "Outbound started conversationId={ConversationId} routeSource={RouteSource} destinationType={DestinationType} exactRoutePreserved={ExactRoutePreserved}",
            conversationId,
            destination.Source,
            destination.DestinationType,
            destination.ExactRoutePreserved);

        foreach (var text in texts)
        {
            linkedToken.ThrowIfCancellationRequested();

            if (!await CanStillAutoReplyAsync(db, conversationId, linkedToken))
            {
                _logger.LogInformation(
                    "Workflow outbound stopped before natural delay; BotMode is not Bot for conversation {ConversationId}",
                    conversationId);
                coordinator?.SetActivity(conversationId, BotReplyActivity.Idle);
                return;
            }

            await ApplyNaturalDelayAsync(
                conversationId,
                destination.Address,
                text,
                options.NaturalTiming,
                client,
                coordinator,
                linkedToken);

            if (!await CanStillAutoReplyAsync(db, conversationId, linkedToken))
            {
                _logger.LogInformation(
                    "Workflow outbound cancelled before send; BotMode is not Bot for conversation {ConversationId}",
                    conversationId);
                await TryClearTypingAsync(client, destination.Address);
                coordinator?.SetActivity(conversationId, BotReplyActivity.Idle);
                return;
            }

            var pendingId = await recorder.CreatePendingBotTextAsync(conversationId, text, linkedToken);
            try
            {
                var externalId = await client.SendTextMessageAsync(destination.Address, text, linkedToken);
                await recorder.FinalizeBotTextAsync(
                    pendingId,
                    externalId,
                    DeliveryStatus.Sent,
                    linkedToken);
                _logger.LogInformation(
                    "Outbound completed conversationId={ConversationId} connectorAccepted=true sockSendMessageCalled=true baileysMessageIdPresent={MessageIdPresent} routeSource={RouteSource} destinationType={DestinationType} exactRoutePreserved={ExactRoutePreserved} selfMatch=false",
                    conversationId,
                    !string.IsNullOrWhiteSpace(externalId),
                    destination.Source,
                    destination.DestinationType,
                    destination.ExactRoutePreserved);
            }
            catch (Exception ex)
            {
                await recorder.FinalizeBotTextAsync(pendingId, null, DeliveryStatus.Failed, CancellationToken.None);
                _logger.LogWarning(
                    ex,
                    "Outbound failed conversationId={ConversationId} routeSource={RouteSource} destinationType={DestinationType}",
                    conversationId,
                    destination.Source,
                    destination.DestinationType);
                throw;
            }

            await TryClearTypingAsync(client, destination.Address);
        }

        coordinator?.SetActivity(conversationId, BotReplyActivity.Idle);
    }

    public async Task TrySendConfigExampleImageAsync(
        Guid conversationId,
        string customerIdentity,
        string? whatsAppDeliveryRoute,
        string imagePath,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        if (WhatsAppEventMapper.IsGroupJid(whatsAppDeliveryRoute ?? customerIdentity))
        {
            _logger.LogWarning("Bot auto-reply skipped for WhatsApp group");
            return;
        }

        var options = _services.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        if (!options.AutoReplyEnabled || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        var db = _services.GetRequiredService<IApplicationDbContext>();
        var conversation = await db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null || !ConversationBotPolicy.CanAutoReply(conversation.BotMode))
        {
            return;
        }

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversation.CustomerId, cancellationToken);
        var testAllowlist = _services.GetRequiredService<IWhatsAppTestAllowlist>();
        var selfIdentity = _services.GetRequiredService<IWhatsAppSelfIdentityStore>();
        var allowlistAddress = whatsAppDeliveryRoute ?? customerIdentity;
        if (!testAllowlist.IsOutboundAllowed(allowlistAddress, customer))
        {
            return;
        }

        var destination = await WhatsAppOutboundDestinationResolver.ResolveAsync(
            db,
            selfIdentity,
            conversationId,
            whatsAppDeliveryRoute,
            customerIdentity,
            cancellationToken);

        if (!testAllowlist.IsOutboundAllowed(destination.Address, customer))
        {
            _logger.LogInformation("Test allowlist / exclusion blocked resolved destination conversationId={ConversationId}", conversationId);
            return;
        }

        if (destination.SelfMatch || selfIdentity.IsSelfJid(destination.Address))
        {
            _logger.LogWarning(
                "Outbound blocked: self destination conversationId={ConversationId} destinationType={DestinationType}",
                conversationId,
                destination.DestinationType);
            return;
        }

        var client = _services.GetService<IWhatsAppClient>();
        if (client is null)
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(imagePath);
            var contentType = Path.GetExtension(imagePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";
            var externalId = await client.SendImageMessageAsync(destination.Address, stream, contentType, caption, cancellationToken);
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var recorder = _services.GetRequiredService<IWhatsAppOutboundRecorder>();
                await recorder.RecordBotTextAsync(conversationId, destination.Address, caption ?? "[config example image]", externalId, cancellationToken);
            }
        }
        catch (NotSupportedException)
        {
            _logger.LogWarning("Config example image send is not supported by the WhatsApp client");
        }
    }

    private async Task ApplyNaturalDelayAsync(
        Guid conversationId,
        string destinationAddress,
        string text,
        NaturalResponseTimingOptions timing,
        IWhatsAppClient client,
        IConversationReplyCoordinator? coordinator,
        CancellationToken cancellationToken)
    {
        if (!timing.Enabled)
        {
            return;
        }

        var delay = NaturalResponseTimingCalculator.ComputeDelay(text, timing);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        coordinator?.SetActivity(conversationId, BotReplyActivity.Typing);

        try
        {
            await client.SendComposingAsync(destinationAddress, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Typing presence failed for conversation {ConversationId}; continuing send", conversationId);
        }

        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await TryClearTypingAsync(client, destinationAddress);
            throw;
        }
    }

    private static async Task TryClearTypingAsync(IWhatsAppClient client, string destinationAddress)
    {
        try
        {
            await client.SendPausedPresenceAsync(destinationAddress);
        }
        catch
        {
            /* presence cleanup is best-effort */
        }
    }

    private static async Task<bool> CanStillAutoReplyAsync(
        IApplicationDbContext db,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var botMode = await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.BotMode)
            .FirstOrDefaultAsync(cancellationToken);

        return ConversationBotPolicy.CanAutoReply(botMode);
    }
}
