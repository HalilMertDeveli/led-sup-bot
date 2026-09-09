using System.Net.Http.Json;
using LedSupBot.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.WhatsApp;

public interface IWhatsAppSelfIdentitySync
{
    Task SyncFromConnectorAsync(CancellationToken cancellationToken = default);
}

public sealed class WhatsAppSelfIdentitySync : IWhatsAppSelfIdentitySync
{
    private readonly HttpClient _http;
    private readonly IWhatsAppSelfIdentityStore _selfIdentity;
    private readonly ILogger<WhatsAppSelfIdentitySync> _logger;

    public WhatsAppSelfIdentitySync(
        HttpClient http,
        IWhatsAppSelfIdentityStore selfIdentity,
        ILogger<WhatsAppSelfIdentitySync> logger)
    {
        _http = http;
        _selfIdentity = selfIdentity;
        _logger = logger;
    }

    public async Task SyncFromConnectorAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("self-jids", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<SelfJidsPayload>(cancellationToken: cancellationToken);
            if (payload?.Jids is null || payload.Jids.Count == 0)
            {
                return;
            }

            _selfIdentity.SetSelfJids(payload.Jids);
            _logger.LogInformation("Synced business self identity from connector count={Count}", _selfIdentity.SelfJids.Count);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Self identity sync skipped");
        }
    }

    private sealed class SelfJidsPayload
    {
        public List<string>? Jids { get; set; }
    }
}
