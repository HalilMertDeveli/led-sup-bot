using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.Workflow;

public interface IWorkflowOutboundDispatcher
{
    Task SendTextsAsync(
        Guid conversationId,
        string customerPhoneE164,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    Task TrySendConfigExampleImageAsync(
        Guid conversationId,
        string customerPhoneE164,
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
        string customerPhoneE164,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return;
        }

        var db = _services.GetRequiredService<IApplicationDbContext>();
        var options = _services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowOptions>>().Value;

        if (!options.AutoReplyEnabled)
        {
            _logger.LogInformation(
                "Workflow auto-reply disabled; skipped {Count} outbound message(s) for conversation {ConversationId}",
                texts.Count,
                conversationId);
            return;
        }

        var client = _services.GetService<IWhatsAppClient>();
        if (client is null)
        {
            _logger.LogWarning("WhatsApp client unavailable; workflow outbound skipped");
            return;
        }

        var recorder = _services.GetRequiredService<IWhatsAppOutboundRecorder>();

        foreach (var text in texts)
        {
            var conversation = await db.Conversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

            if (conversation is null || !ConversationBotPolicy.CanAutoReply(conversation.BotMode))
            {
                _logger.LogInformation(
                    "Workflow outbound stopped before send; BotMode={BotMode}",
                    conversation?.BotMode.ToString() ?? "missing");
                return;
            }

            var externalId = await client.SendTextMessageAsync(customerPhoneE164, text, cancellationToken);
            await recorder.RecordBotTextAsync(customerPhoneE164, text, externalId, cancellationToken);
        }
    }

    public async Task TrySendConfigExampleImageAsync(
        Guid conversationId,
        string customerPhoneE164,
        string imagePath,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        var options = _services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowOptions>>().Value;
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
            var externalId = await client.SendImageMessageAsync(customerPhoneE164, stream, contentType, caption, cancellationToken);
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var recorder = _services.GetRequiredService<IWhatsAppOutboundRecorder>();
                await recorder.RecordBotTextAsync(customerPhoneE164, caption ?? "[config example image]", externalId, cancellationToken);
            }
        }
        catch (NotSupportedException)
        {
            _logger.LogWarning("Config example image send is not supported by the WhatsApp client");
        }
    }
}
