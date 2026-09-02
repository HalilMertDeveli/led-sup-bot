using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using LedSupBot.Desktop.Models;

namespace LedSupBot.Desktop.Services;

public sealed class LedSupBotApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies;
    private readonly HttpClientHandler _handler;

    public LedSupBotApiClient(string apiBaseUrl)
    {
        _cookies = new CookieContainer();
        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
        };

        _http = new HttpClient(_handler)
        {
            BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public CookieContainer Cookies => _cookies;

    public HttpMessageHandler MessageHandler => _handler;

    public async Task<AuthStatusResponse> GetAuthStatusAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<AuthStatusResponse>("api/admin/auth/status", cancellationToken)
        ?? new AuthStatusResponse();

    public async Task LoginAsync(string password, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/admin/auth/login", new { password }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LedSupBotApiException("Giriş başarısız. Şifreyi kontrol edin.", (int)response.StatusCode);
        }
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _http.PostAsync("api/admin/auth/logout", null, cancellationToken);

    public async Task<SystemStatusResponse> GetSystemStatusAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<SystemStatusResponse>("api/admin/system/status", cancellationToken)
        ?? new SystemStatusResponse();

    public async Task<string?> GetFoundationWhatsAppStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetFoundationStatusAsync(cancellationToken);
        return status?.WhatsAppConnection;
    }

    public async Task<FoundationStatusResponse?> GetFoundationStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync("api/foundation/status", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<FoundationStatusResponse>(stream, JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ConversationListResponse> GetConversationsAsync(
        string? mode = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
        {
            query.Add($"mode={Uri.EscapeDataString(mode)}");
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        var path = "api/admin/conversations";
        if (query.Count > 0)
        {
            path += "?" + string.Join("&", query);
        }

        return await GetAsync<ConversationListResponse>(path, cancellationToken)
               ?? new ConversationListResponse();
    }

    public async Task<ConversationDetailDto?> GetConversationDetailAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
        {
            return null;
        }

        return await GetAsync<ConversationDetailDto>($"api/admin/conversations/{conversationId:D}", cancellationToken);
    }

    public async Task<MessagesResponse?> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
        {
            return null;
        }

        return await GetAsync<MessagesResponse>($"api/admin/conversations/{conversationId:D}/messages", cancellationToken);
    }

    public async Task<SendMessageResponse> SendTextMessageAsync(
        Guid conversationId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
        {
            throw new LedSupBotApiException("Aktif konuşma seçilmedi.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new LedSupBotApiException("Mesaj metni boş olamaz.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(
                $"api/admin/conversations/{conversationId:D}/messages",
                new SendMessageRequest { Text = text },
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LedSupBotApiException("API bağlantısı yok.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LedSupBotApiException("Mesaj gönderimi zaman aşımına uğradı.", inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var message = status switch
            {
                404 => "Konuşma bulunamadı.",
                503 => "WhatsApp servisi şu an kullanılamıyor.",
                _ => "Mesaj gönderilemedi.",
            };
            throw new LedSupBotApiException(message, status);
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<SendMessageResponse>(stream, JsonOptions, cancellationToken);
            return result ?? new SendMessageResponse();
        }
        catch (JsonException ex)
        {
            throw new LedSupBotApiException("API yanıtı okunamadı.", (int)response.StatusCode, ex);
        }
    }

    public async Task<CompactFocusResponse> GetCompactFocusAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<CompactFocusResponse>("api/admin/compact/focus", cancellationToken)
        ?? new CompactFocusResponse();

    public Task PauseBotAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        PostModeChangeAsync("pause", conversationId, cancellationToken);

    public Task ResumeBotAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        PostModeChangeAsync("resume", conversationId, cancellationToken);

    public Task TakeOverAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        PostModeChangeAsync("takeover", conversationId, cancellationToken);

    public Task DismissAnyDeskAlertAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        PostModeChangeAsync("dismiss-anydesk-alert", conversationId, cancellationToken);

    private async Task PostModeChangeAsync(string action, Guid conversationId, CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty)
        {
            throw new LedSupBotApiException("Aktif müşteri bulunamadı.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(
                $"api/admin/conversations/{conversationId:D}/{action}",
                null,
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LedSupBotApiException("API bağlantısı yok.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LedSupBotApiException("API bağlantısı zaman aşımına uğradı.", inner: ex);
        }

        await EnsureSuccessAsync(response);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(path, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LedSupBotApiException("API bağlantısı yok.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LedSupBotApiException("API bağlantısı zaman aşımına uğradı.", inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new LedSupBotApiException("API yanıtı okunamadı.", (int)response.StatusCode, ex);
        }
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        var status = (int)response.StatusCode;
        var message = status switch
        {
            401 or 403 => "Yetkilendirme başarısız.",
            404 => "Aktif konuşma bulunamadı.",
            409 => "Bot durumu değiştirilemedi.",
            >= 500 => "API bağlantısı yok.",
            _ => "Bot durumu değiştirilemedi.",
        };

        throw new LedSupBotApiException(message, status);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }
}
