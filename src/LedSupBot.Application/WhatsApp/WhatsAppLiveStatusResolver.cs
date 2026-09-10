using LedSupBot.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LedSupBot.Application.WhatsApp;

/// <summary>Resolves WhatsApp connection status from the live connector when available.</summary>
public static class WhatsAppLiveStatusResolver
{
    public static async Task<WhatsAppConnectionStatus> GetCurrentAsync(
        IServiceProvider services,
        IWhatsAppConnectionStateStore stateStore,
        CancellationToken cancellationToken = default)
    {
        var client = services.GetService<IWhatsAppClient>();
        if (client is null)
        {
            return stateStore.Current;
        }

        try
        {
            return await client.GetConnectionStatusAsync(cancellationToken);
        }
        catch
        {
            return stateStore.Current;
        }
    }
}
