using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.WhatsApp;

public interface IWhatsAppOutboundRecorder
{
    Task RecordBotTextAsync(
        string toPhoneE164,
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

    public async Task RecordBotTextAsync(
        string toPhoneE164,
        string body,
        string? externalMessageId,
        CancellationToken cancellationToken = default)
    {
        var db = _services.GetService<IApplicationDbContext>();
        if (db is null)
        {
            _logger.LogWarning("Database unavailable; bot outbound message was not recorded for takeover correlation");
            return;
        }

        if (string.IsNullOrWhiteSpace(externalMessageId))
        {
            _logger.LogWarning("Bot send returned no external message id; takeover correlation may be weaker");
            return;
        }

        var exists = await db.Messages.AnyAsync(m => m.ExternalMessageId == externalMessageId, cancellationToken);
        if (exists)
        {
            return;
        }

        var phone = WhatsAppEventMapper.NormalizePhone(toPhoneE164);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone, cancellationToken);
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

        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            MessageType = MessageType.Text,
            Content = body,
            ExternalMessageId = externalMessageId,
            DeliveryStatus = DeliveryStatus.Sent,
            SentBy = MessageSender.Bot
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogInformation("Bot outbound already recorded (idempotent)");
        }
    }
}
