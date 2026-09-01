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

        var client = _services.GetRequiredService<IWhatsAppClient>();
        var recorder = _services.GetRequiredService<IWhatsAppOutboundRecorder>();

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
                customerPhoneE164,
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
                await TryClearTypingAsync(client, customerPhoneE164);
                coordinator?.SetActivity(conversationId, BotReplyActivity.Idle);
                return;
            }

            var externalId = await client.SendTextMessageAsync(customerPhoneE164, text, linkedToken);
            await recorder.RecordBotTextAsync(customerPhoneE164, text, externalId, linkedToken);
            await TryClearTypingAsync(client, customerPhoneE164);
        }

        coordinator?.SetActivity(conversationId, BotReplyActivity.Idle);
    }

    public async Task TrySendConfigExampleImageAsync(
        Guid conversationId,
        string customerPhoneE164,
        string imagePath,
        string? caption,
        CancellationToken cancellationToken = default)
    {
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

    private async Task ApplyNaturalDelayAsync(
        Guid conversationId,
        string customerPhoneE164,
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
            await client.SendComposingAsync(customerPhoneE164, cancellationToken);
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
            await TryClearTypingAsync(client, customerPhoneE164);
            throw;
        }
    }

    private static async Task TryClearTypingAsync(IWhatsAppClient client, string customerPhoneE164)
    {
        try
        {
            await client.SendPausedPresenceAsync(customerPhoneE164);
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
