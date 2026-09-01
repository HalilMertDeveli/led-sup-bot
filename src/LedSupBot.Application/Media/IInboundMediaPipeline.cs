using LedSupBot.Application.Abstractions;
using LedSupBot.Domain.Entities;

namespace LedSupBot.Application.Media;

public interface IInboundMediaPipeline
{
    Task<InboundMediaProcessResult> ProcessInboundImageAsync(
        Message message,
        TechnicalSupportCase supportCase,
        WhatsAppInboundEvent inboundEvent,
        CancellationToken cancellationToken = default);
}
