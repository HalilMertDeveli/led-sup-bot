using System.Net.Http.Json;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
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
        var destination = NormalizeDestination(toPhoneE164);
        var destinationType = WhatsAppEventMapper.IsLidJid(destination)
            ? "lid"
            : WhatsAppEventMapper.IsGroupJid(destination)
                ? "g.us"
                : "s.whatsapp.net";

        using var response = await _http.PostAsJsonAsync(
            "send/text",
            new { to = destination, text = body },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Connector send failed connectorHttp={ConnectorHttp} destinationType={DestinationType}",
                (int)response.StatusCode,
                destinationType);
            throw new InvalidOperationException($"Connector send failed: {(int)response.StatusCode}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SendPayload>(cancellationToken: cancellationToken);
        var messageIdPresent = payload?.BaileysMessageIdPresent == true ||
                               !string.IsNullOrWhiteSpace(payload?.ExternalMessageId);

        _logger.LogInformation(
            "Connector send accepted connectorHttp={ConnectorHttp} destinationType={DestinationType} baileysMessageIdPresent={MessageIdPresent} exactRoutePreserved={ExactRoutePreserved} sockSendMessageCalled=true",
            (int)response.StatusCode,
            payload?.DestinationType ?? destinationType,
            messageIdPresent,
            payload?.ExactRoutePreserved ?? true);

        if (!messageIdPresent)
        {
            throw new InvalidOperationException("Connector accepted send but Baileys message id was not produced.");
        }

        return payload!.ExternalMessageId;
    }

    public async Task SendComposingAsync(string toPhoneE164, CancellationToken cancellationToken = default)
    {
        await SendPresenceAsync(toPhoneE164, "composing", cancellationToken);
    }

    public async Task SendPausedPresenceAsync(string toPhoneE164, CancellationToken cancellationToken = default)
    {
        await SendPresenceAsync(toPhoneE164, "paused", cancellationToken);
    }

    private async Task SendPresenceAsync(string toPhoneE164, string state, CancellationToken cancellationToken)
    {
        var destination = NormalizeDestination(toPhoneE164);

        using var response = await _http.PostAsJsonAsync(
            "send/presence",
            new { to = destination, state },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Connector presence {State} failed with status {Status}", state, (int)response.StatusCode);
        }
    }

    public async Task<string?> SendImageMessageAsync(
        string toPhoneE164,
        Stream image,
        string contentType,
        string? caption = null,
        CancellationToken cancellationToken = default)
    {
        var destination = NormalizeDestination(toPhoneE164);
        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, cancellationToken);
        var base64 = Convert.ToBase64String(ms.ToArray());

        using var response = await _http.PostAsJsonAsync(
            "send/image",
            new
            {
                to = destination,
                caption,
                imageBase64 = base64
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Connector image send failed with status {Status}", (int)response.StatusCode);
            throw new InvalidOperationException($"Connector image send failed: {(int)response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<SendPayload>(cancellationToken: cancellationToken);
        return result?.ExternalMessageId;
    }

    public async Task<string?> SendDocumentMessageAsync(
        string toPhoneE164, Stream document, string contentType, string fileName,
        string? caption = null, CancellationToken cancellationToken = default)
    {
        var destination = NormalizeDestination(toPhoneE164);
        using var ms = new MemoryStream();
        await document.CopyToAsync(ms, cancellationToken);
        using var response = await _http.PostAsJsonAsync("send/document", new
        {
            to = destination,
            caption,
            contentType,
            fileName = Path.GetFileName(fileName),
            documentBase64 = Convert.ToBase64String(ms.ToArray())
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Connector document send failed: {(int)response.StatusCode}");
        var result = await response.Content.ReadFromJsonAsync<SendPayload>(cancellationToken: cancellationToken);
        return result?.ExternalMessageId;
    }

    public async Task<WhatsAppMediaDownloadResult> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "download/media",
            new { externalMessageId = mediaId },
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("media_not_found");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Connector media download failed with status {Status}", (int)response.StatusCode);
            throw new InvalidOperationException($"Connector media download failed: {(int)response.StatusCode}");
        }

        var mimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new WhatsAppMediaDownloadResult(mediaId, mimeType, content);
    }

    private static string NormalizeDestination(string toAddress)
    {
        if (toAddress.Contains('@', StringComparison.Ordinal))
        {
            return WhatsAppEventMapper.NormalizeWhatsAppAddress(toAddress.Trim());
        }

        return new string(toAddress.Where(char.IsDigit).ToArray());
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
        public bool Ok { get; set; }

        public string? ExternalMessageId { get; set; }

        public string? DestinationType { get; set; }

        public bool BaileysMessageIdPresent { get; set; }

        public bool ExactRoutePreserved { get; set; }

        public bool SockSendMessageCalled { get; set; }
    }
}
