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
            throw new InvalidOperationException("Giriş başarısız. Şifreyi kontrol edin.");
        }
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _http.PostAsync("api/admin/auth/logout", null, cancellationToken);

    public async Task<SystemStatusResponse> GetSystemStatusAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<SystemStatusResponse>("api/admin/system/status", cancellationToken)
        ?? new SystemStatusResponse();

    public async Task<CompactFocusResponse> GetCompactFocusAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<CompactFocusResponse>("api/admin/compact/focus", cancellationToken)
        ?? new CompactFocusResponse();

    public async Task PauseBotAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"api/admin/conversations/{conversationId:D}/pause", null, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task ResumeBotAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"api/admin/conversations/{conversationId:D}/resume", null, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task TakeOverAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"api/admin/conversations/{conversationId:D}/takeover", null, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task DismissAnyDeskAlertAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"api/admin/conversations/{conversationId:D}/dismiss-anydesk-alert", null, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = response.ReasonPhrase ?? "İşlem başarısız.";
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                detail = body;
            }
        }
        catch
        {
            /* ignore */
        }

        throw new InvalidOperationException(detail);
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
