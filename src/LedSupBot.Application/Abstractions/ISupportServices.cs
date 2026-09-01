using LedSupBot.Domain.Enums;
using LedSupBot.Domain.ValueObjects;

namespace LedSupBot.Application.Abstractions;

public interface ICustomerService
{
    Task<Guid> GetOrCreateByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken = default);
}

public interface IConversationService
{
    Task<Guid> GetOrCreateActiveAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task SetBotModeAsync(Guid conversationId, BotMode mode, CancellationToken cancellationToken = default);
}

public interface IMessageService
{
    Task<Guid> RecordInboundAsync(Guid conversationId, string? body, MessageType type, CancellationToken cancellationToken = default);

    Task<Guid> RecordOutboundAsync(Guid conversationId, string body, MessageSender sentBy, CancellationToken cancellationToken = default);
}

public interface IConversationStateService
{
    Task<ConversationStage> GetCurrentStateAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task TransitionAsync(Guid conversationId, ConversationStage toState, string reason, CancellationToken cancellationToken = default);
}

public interface ITechnicalSupportService
{
    Task<Guid> GetOrCreateCaseAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<string> BuildTechnicianSummaryAsync(Guid caseId, CancellationToken cancellationToken = default);
}
