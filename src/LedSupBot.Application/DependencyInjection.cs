using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IWhatsAppConnectionStateStore, WhatsAppConnectionStateStore>();
        services.AddScoped<IWhatsAppInboundEventHandler, WhatsAppInboundEventHandler>();
        services.AddScoped<IWhatsAppOutboundRecorder, WhatsAppOutboundRecorder>();
        return services;
    }
}

internal sealed class WhatsAppConnectionStateStore : IWhatsAppConnectionStateStore
{
    private WhatsAppConnectionStatus _current = WhatsAppConnectionStatus.Disconnected;

    public WhatsAppConnectionStatus Current => _current;

    public void Set(WhatsAppConnectionStatus status) => _current = status;
}
