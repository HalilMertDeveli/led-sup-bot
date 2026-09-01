using System.Net.Http.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.WhatsApp;

public sealed class ConnectorWhatsAppClient : IWhatsAppClient
{
    private readonly HttpClient _http;
    private readonly WhatsAppConnectorOptions _options;
    private readonly IWhatsAppConnectionStateStore _stateStore;
    private readonly ILogger<ConnectorWhatsAppClient> _logger;

    public ConnectorWhatsAppClient(
        HttpClient http,
        IOptions<WhatsAppConnectorOptions> options,
        IWhatsAppConnectionStateStore stateStore,
        ILogger<ConnectorWhatsAppClient> logger)
    {
        _http = http;
        _options = options.Value;
        _stateStore = stateStore;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("X-Connector-Secret");
        _http.DefaultRequestHeaders.Add("X-Connector-Secret", _options.SharedSecret);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Connector process owns the session; ASP.NET only observes status.
        _logger.LogInformation("Connect requested — ensure the Node connector process is running");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnect requested — stop the Node connector process to unlink session usage");
        _stateStore.Set(WhatsAppConnectionStatus.Disconnected);
        return Task.CompletedTask;
    }

    public async Task<WhatsAppConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("status", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return WhatsAppConnectionStatus.Error;
            }

            var payload = await response.Content.ReadFromJsonAsync<StatusPayload>(cancellationToken: cancellationToken);
            var mapped = MapStatus(payload?.Status);
            _stateStore.Set(mapped);
            return mapped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query connector status");
            return WhatsAppConnectionStatus.Disconnected;
        }
    }

    public async Task<string?> SendTextMessageAsync(string toPhoneE164, string body, CancellationToken cancellationToken = default)
    {
        var digits = new string(toPhoneE164.Where(char.IsDigit).ToArray());
        using var response = await _http.PostAsJsonAsync(
            "send/text",
            new { to = digits, text = body },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Connector send failed with status {Status}", (int)response.StatusCode);
            throw new InvalidOperationException($"Connector send failed: {(int)response.StatusCode}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SendPayload>(cancellationToken: cancellationToken);
        return payload?.ExternalMessageId;
    }

    public Task<string?> SendImageMessageAsync(
        string toPhoneE164,
        Stream image,
        string contentType,
        string? caption = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Image send via connector is deferred to a later phase.");
    }

    public Task<WhatsAppMediaDownloadResult> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Media download pipeline belongs to Phase 7.");
    }

    private static WhatsAppConnectionStatus MapStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "CONNECTED" => WhatsAppConnectionStatus.Connected,
        "CONNECTING" => WhatsAppConnectionStatus.Connecting,
        "AUTHENTICATION_REQUIRED" => WhatsAppConnectionStatus.AuthenticationRequired,
        "ERROR" => WhatsAppConnectionStatus.Error,
        _ => WhatsAppConnectionStatus.Disconnected
    };

    private sealed class StatusPayload
    {
        public string? Status { get; set; }
    }

    private sealed class SendPayload
    {
        public string? ExternalMessageId { get; set; }
    }
}
