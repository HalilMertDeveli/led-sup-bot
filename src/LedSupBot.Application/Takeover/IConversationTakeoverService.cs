using LedSupBot.Domain.Enums;

namespace LedSupBot.Application.Takeover;

public interface IConversationTakeoverService
{
    Task<TakeoverOperationResult> TakeOverAsync(
        Guid conversationId,
        TakeoverChangeSource source,
        CancellationToken cancellationToken = default);

    Task<TakeoverOperationResult> PauseAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<TakeoverOperationResult> ResumeBotAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}

public sealed record TakeoverOperationResult(
    bool Found,
    bool Changed,
    BotMode PreviousMode,
    BotMode CurrentMode,
    ConversationStage CurrentStage,
    ConversationStatus CurrentStatus);
