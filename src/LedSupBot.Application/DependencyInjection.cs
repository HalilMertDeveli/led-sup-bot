using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Workflow.Knowledge;
using LedSupBot.Application.PanelFiles;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IWhatsAppConnectionStateStore, WhatsAppConnectionStateStore>();
        services.AddSingleton<IWhatsAppSelfIdentityStore, WhatsAppSelfIdentityStore>();
        services.AddSingleton<IWhatsAppTestAllowlist, WhatsAppTestAllowlist>();
        services.AddSingleton<ITechnicalKnowledgeProvider, DefaultTechnicalKnowledgeProvider>();
        services.AddScoped<IPackageCodeFastDeliveryService, PackageCodeFastDeliveryService>();
        services.AddScoped<TechnicalKnowledgeGroundingService>();
        services.AddScoped<IWhatsAppInboundEventHandler, WhatsAppInboundEventHandler>();
        services.AddScoped<IWhatsAppOutboundRecorder, WhatsAppOutboundRecorder>();
        services.AddSingleton<IAdminEventNotifier, NullAdminEventNotifier>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();
        return services;
    }
}

internal sealed class WhatsAppConnectionStateStore : IWhatsAppConnectionStateStore
{
    private WhatsAppConnectionStatus _current = WhatsAppConnectionStatus.Disconnected;

    public WhatsAppConnectionStatus Current => _current;

    public void Set(WhatsAppConnectionStatus status) => _current = status;
}
