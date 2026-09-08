using LedSupBot.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Application.Workflow;

public sealed class ConversationReplyCoordinator : IConversationReplyCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowOptions _options;
    private readonly ILogger<ConversationReplyCoordinator> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ConversationReplyState> _states = new();

    public ConversationReplyCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkflowOptions> options,
        ILogger<ConversationReplyCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public void EnqueueCustomerInbound(PendingCustomerInbound inbound)
    {
        CancellationToken debounceToken;
        lock (_sync)
        {
            var state = GetOrCreateState(inbound.ConversationId);
            CancelReplyPipelineLocked(state);
            state.Pending.Add(inbound);

            state.DebounceCts?.Cancel();
            state.DebounceCts?.Dispose();
            state.DebounceCts = new CancellationTokenSource();
            debounceToken = state.DebounceCts.Token;
        }

        var window = TimeSpan.FromSeconds(Math.Max(0.5, _options.InboundCoalescing.WindowSeconds));
        if (!_options.InboundCoalescing.Enabled || window <= TimeSpan.Zero)
        {
            _ = RunReplyPipelineAsync(inbound.ConversationId, CancellationToken.None);
            return;
        }

        _ = DebounceThenRunAsync(inbound.ConversationId, window, debounceToken);
    }

    public void CancelPendingReply(Guid conversationId)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(conversationId, out var state))
            {
                return;
            }

            state.DebounceCts?.Cancel();
            state.DebounceCts?.Dispose();
            state.DebounceCts = null;
            state.Pending.Clear();
            CancelReplyPipelineLocked(state);
            state.Activity = BotReplyActivity.Idle;
        }
    }

    public BotReplyActivity GetActivity(Guid conversationId)
    {
        lock (_sync)
        {
            return _states.TryGetValue(conversationId, out var state)
                ? state.Activity
                : BotReplyActivity.Idle;
        }
    }

    public void SetActivity(Guid conversationId, BotReplyActivity activity)
    {
        lock (_sync)
        {
            var state = GetOrCreateState(conversationId);
            state.Activity = activity;
        }
    }

    public CancellationToken LinkReplyCancellation(Guid conversationId, CancellationToken callerToken)
    {
        lock (_sync)
        {
            var state = GetOrCreateState(conversationId);
            state.ReplyCts ??= new CancellationTokenSource();
            return CancellationTokenSource.CreateLinkedTokenSource(callerToken, state.ReplyCts.Token).Token;
        }
    }

    private async Task DebounceThenRunAsync(Guid conversationId, TimeSpan window, CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(window, debounceToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunReplyPipelineAsync(conversationId, debounceToken);
    }

    private async Task RunReplyPipelineAsync(Guid conversationId, CancellationToken debounceToken)
    {
        List<PendingCustomerInbound> batch;
        CancellationToken pipelineToken;

        lock (_sync)
        {
            if (!_states.TryGetValue(conversationId, out var state) || state.Pending.Count == 0)
            {
                return;
            }

            batch = state.Pending.ToList();
            state.Pending.Clear();
            CancelReplyPipelineLocked(state);
            state.ReplyCts = CancellationTokenSource.CreateLinkedTokenSource(debounceToken);
            pipelineToken = state.ReplyCts.Token;
            state.Activity = BotReplyActivity.Preparing;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var workflow = scope.ServiceProvider.GetRequiredService<IConversationWorkflowService>();
            var inbound = BuildCoalescedInbound(batch);
            await workflow.ProcessCustomerInboundAsync(inbound, pipelineToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Pending bot reply cancelled for conversation {ConversationId}", conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process coalesced inbound for conversation {ConversationId}", conversationId);
            try
            {
                using var fallbackScope = _scopeFactory.CreateScope();
                var last = batch[^1];
                await fallbackScope.ServiceProvider.GetRequiredService<IWorkflowOutboundDispatcher>().SendTextsAsync(
                    conversationId, last.CustomerPhoneE164, last.WhatsAppDeliveryRoute,
                    ["Mesajınızı aldım ancak yanıtı hazırlarken geçici bir sorun oluştu. Kısa şekilde tekrar yazar mısınız?"],
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Provider failure fallback could not be delivered for conversation {ConversationId}", conversationId);
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_states.TryGetValue(conversationId, out var state))
                {
                    CancelReplyPipelineLocked(state);
                    if (state.Activity != BotReplyActivity.Typing)
                    {
                        state.Activity = BotReplyActivity.Idle;
                    }
                }
            }
        }
    }

    private static WorkflowInboundMessage BuildCoalescedInbound(IReadOnlyList<PendingCustomerInbound> batch)
    {
        var last = batch[^1];
        var textSegments = batch
            .Select(m => m.TextContent?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .ToList();

        var messageType = batch.Any(m => m.MessageType == MessageType.Image)
            ? MessageType.Image
            : last.MessageType;

        return new WorkflowInboundMessage(
            last.ConversationId,
            last.CustomerPhoneE164,
            last.MessageId,
            last.ExternalMessageId,
            messageType,
            textSegments.Count > 0 ? string.Join('\n', textSegments) : last.TextContent,
            batch.Select(m => m.MessageId).ToList(),
            last.WhatsAppDeliveryRoute);
    }

    private ConversationReplyState GetOrCreateState(Guid conversationId)
    {
        if (!_states.TryGetValue(conversationId, out var state))
        {
            state = new ConversationReplyState();
            _states[conversationId] = state;
        }

        return state;
    }

    private static void CancelReplyPipelineLocked(ConversationReplyState state)
    {
        state.ReplyCts?.Cancel();
        state.ReplyCts?.Dispose();
        state.ReplyCts = null;
    }

    private sealed class ConversationReplyState
    {
        public List<PendingCustomerInbound> Pending { get; } = [];
        public CancellationTokenSource? DebounceCts { get; set; }
        public CancellationTokenSource? ReplyCts { get; set; }
        public BotReplyActivity Activity { get; set; } = BotReplyActivity.Idle;
    }
}
