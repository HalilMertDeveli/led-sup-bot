using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.WhatsApp;

public interface IWhatsAppOutboundRecorder
{
    Task<Guid> CreatePendingBotTextAsync(
        Guid conversationId,
        string body,
        CancellationToken cancellationToken = default);

    Task FinalizeBotTextAsync(
        Guid messageId,
        string? externalMessageId,
        DeliveryStatus deliveryStatus,
        CancellationToken cancellationToken = default);

    Task RecordBotTextAsync(
        Guid? conversationId,
        string destinationAddress,
        string body,
        string? externalMessageId,
        CancellationToken cancellationToken = default);
}

public sealed class WhatsAppOutboundRecorder : IWhatsAppOutboundRecorder
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WhatsAppOutboundRecorder> _logger;

    public WhatsAppOutboundRecorder(IServiceProvider services, ILogger<WhatsAppOutboundRecorder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<Guid> CreatePendingBotTextAsync(
        Guid conversationId,
        string body,
        CancellationToken cancellationToken = default)
    {
        var db = RequireDb();
        var message = new Message
        {
            ConversationId = conversationId,
            Direction = MessageDirection.Outbound,
            MessageType = MessageType.Text,
            Content = body,
            DeliveryStatus = DeliveryStatus.Pending,
            SentBy = MessageSender.Bot
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var notifier = _services.GetService<IAdminEventNotifier>();
        if (notifier is not null)
        {
            await notifier.NotifyMessageReceivedAsync(conversationId, message.Id, cancellationToken);
            await notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);
        }

        return message.Id;
    }

    public async Task FinalizeBotTextAsync(
        Guid messageId,
        string? externalMessageId,
        DeliveryStatus deliveryStatus,
        CancellationToken cancellationToken = default)
    {
        var db = RequireDb();
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null)
        {
            _logger.LogWarning("Pending bot outbound message {MessageId} not found for finalize", messageId);
            return;
        }

        message.DeliveryStatus = deliveryStatus;
        if (!string.IsNullOrWhiteSpace(externalMessageId))
        {
            message.ExternalMessageId = externalMessageId;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            var notifier = _services.GetService<IAdminEventNotifier>();
            if (notifier is not null)
            {
                await notifier.NotifyMessageReceivedAsync(message.ConversationId, message.Id, cancellationToken);
                await notifier.NotifyConversationUpdatedAsync(message.ConversationId, cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            _logger.LogInformation("Bot outbound finalize conflict (idempotent) messageId={MessageId}", messageId);
        }
    }

    public async Task RecordBotTextAsync(
        Guid? conversationId,
        string destinationAddress,
        string body,
        string? externalMessageId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId is Guid convId)
        {
            if (string.IsNullOrWhiteSpace(externalMessageId))
            {
                var pendingId = await CreatePendingBotTextAsync(convId, body, cancellationToken);
                await FinalizeBotTextAsync(pendingId, null, DeliveryStatus.Failed, cancellationToken);
                return;
            }

            var pending = await CreatePendingBotTextAsync(convId, body, cancellationToken);
            await FinalizeBotTextAsync(pending, externalMessageId, DeliveryStatus.Sent, cancellationToken);
            return;
        }

        _logger.LogWarning(
            "Bot outbound recorded without conversationId; delivery correlation may be weaker destinationType={DestinationType}",
            destinationAddress.Contains('@') ? "jid" : "e164");
    }

    private IApplicationDbContext RequireDb()
    {
        var db = _services.GetService<IApplicationDbContext>();
        if (db is null)
        {
            throw new InvalidOperationException("Database unavailable; bot outbound message was not recorded");
        }

        return db;
    }
}
