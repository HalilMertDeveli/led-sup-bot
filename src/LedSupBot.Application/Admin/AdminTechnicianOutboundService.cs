using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.Admin;

public interface IAdminTechnicianOutboundService
{
    Task<(SendMessageResponse? Success, string? ErrorCode)> SendTextAsync(
        Guid conversationId,
        string text,
        CancellationToken cancellationToken = default);
}

public sealed class AdminTechnicianOutboundService : IAdminTechnicianOutboundService
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly IConversationTakeoverService _takeover;
    private readonly IWhatsAppSelfIdentityStore _selfIdentity;
    private readonly IAdminEventNotifier _notifier;
    private readonly ILogger<AdminTechnicianOutboundService> _logger;

    public AdminTechnicianOutboundService(
        IApplicationDbContext db,
        IWhatsAppClient whatsAppClient,
        IConversationTakeoverService takeover,
        IWhatsAppSelfIdentityStore selfIdentity,
        IAdminEventNotifier notifier,
        ILogger<AdminTechnicianOutboundService> logger)
    {
        _db = db;
        _whatsAppClient = whatsAppClient;
        _takeover = takeover;
        _selfIdentity = selfIdentity;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<(SendMessageResponse? Success, string? ErrorCode)> SendTextAsync(
        Guid conversationId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, "text_required");
        }

        var conversation = await _db.Conversations
            .Include(c => c.Customer)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return (null, "conversation_not_found");
        }

        if (conversation.BotMode != BotMode.Human)
        {
            var takeoverResult = await _takeover.TakeOverAsync(
                conversationId,
                TakeoverChangeSource.AdminTakeover,
                cancellationToken);

            if (!takeoverResult.Found)
            {
                return (null, "conversation_not_found");
            }

            conversation = await _db.Conversations
                .Include(c => c.Customer)
                .SingleAsync(c => c.Id == conversationId, cancellationToken);

            if (conversation.BotMode != BotMode.Human)
            {
                _logger.LogError(
                    "Takeover did not persist Human mode for conversation {ConversationId}",
                    conversationId);
                return (null, "takeover_failed");
            }
        }

        string? externalId;
        try
        {
            var destination = await WhatsAppOutboundDestinationResolver.ResolveAsync(
                _db,
                _selfIdentity,
                conversationId,
                conversation.LastInboundWhatsAppAddress,
                conversation.Customer.PhoneNumber,
                cancellationToken);

            externalId = await _whatsAppClient.SendTextMessageAsync(
                destination.Address,
                text.Trim(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Technician outbound send failed for conversation {ConversationId}", conversationId);
            return (null, "connector_unavailable");
        }

        var message = new Message
        {
            ConversationId = conversationId,
            Direction = MessageDirection.Outbound,
            MessageType = MessageType.Text,
            Content = text.Trim(),
            ExternalMessageId = externalId,
            DeliveryStatus = string.IsNullOrWhiteSpace(externalId) ? DeliveryStatus.Failed : DeliveryStatus.Sent,
            SentBy = MessageSender.Technician,
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);

        await _notifier.NotifyMessageReceivedAsync(conversationId, message.Id, cancellationToken);
        await _notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);

        return (new SendMessageResponse(message.Id, externalId, message.DeliveryStatus), null);
    }
}
