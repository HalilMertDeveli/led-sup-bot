using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text.Json;
using LedSupBot.Application.PanelFiles;
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
            Timeout = TimeSpan.FromMinutes(5),
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

    public async Task ResetWhatsAppSessionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync("api/admin/system/whatsapp/reset-session", null, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public Task<WhatsAppPairingQrResponse?> GetWhatsAppPairingQrAsync(CancellationToken cancellationToken = default) =>
        GetAsync<WhatsAppPairingQrResponse>("api/admin/system/whatsapp/pairing-qr", cancellationToken);

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

    public Task<CaseMemoryDto?> CreateMemoryDraftAsync(Guid caseId, CancellationToken ct = default) =>
        SendMemoryAsync(HttpMethod.Post, $"api/admin/support-cases/{caseId:D}/memory-draft", null, ct);
    public Task<CaseMemoryDto?> UpdateMemoryDraftAsync(Guid id, CaseMemoryEditRequest edit, CancellationToken ct = default) =>
        SendMemoryAsync(HttpMethod.Put, $"api/admin/case-memories/{id:D}", edit, ct);
    public Task<CaseMemoryDto?> VerifyMemoryAsync(Guid id, CancellationToken ct = default) =>
        SendMemoryAsync(HttpMethod.Post, $"api/admin/case-memories/{id:D}/verify", null, ct);
    public Task<CaseMemoryDto?> RejectMemoryAsync(Guid id, CancellationToken ct = default) =>
        SendMemoryAsync(HttpMethod.Post, $"api/admin/case-memories/{id:D}/reject", null, ct);

    private async Task<CaseMemoryDto?> SendMemoryAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new LedSupBotApiException("Vaka hafızası işlemi tamamlanamadı.", (int)response.StatusCode);
        return await response.Content.ReadFromJsonAsync<CaseMemoryDto>(JsonOptions, ct);
    }

    public async Task<PanelPackageListResponse> GetPanelPackagesAsync(string? search, string? pitch, string? code, string? status, string? readiness, CancellationToken ct = default)
    {
        var query = new List<string>(); AddQueryValue(query, "search", search); AddQueryValue(query, "pitch", pitch);
        AddQueryValue(query, "code", code); AddQueryValue(query, "status", status is "all" ? null : status);
        AddQueryValue(query, "readiness", readiness is "all" ? null : readiness);
        return await GetAsync<PanelPackageListResponse>("api/admin/panel-file-packages" + (query.Count > 0 ? "?" + string.Join("&", query) : ""), ct) ?? new();
    }
    public Task<PanelPackageDto?> CreatePanelPackageAsync(PanelPackageWriteRequest body, CancellationToken ct = default) => SendPanelAsync(HttpMethod.Post, "api/admin/panel-file-packages", body, ct);
    public Task<PanelPackageDto?> UpdatePanelPackageAsync(Guid id, PanelPackageWriteRequest body, CancellationToken ct = default) => SendPanelAsync(HttpMethod.Put, $"api/admin/panel-file-packages/{id:D}", body, ct);
    public Task<PanelPackageDto?> ActivatePanelPackageAsync(Guid id, CancellationToken ct = default) => SendPanelAsync(HttpMethod.Post, $"api/admin/panel-file-packages/{id:D}/activate", null, ct);
    public Task<PanelPackageDto?> ArchivePanelPackageAsync(Guid id, CancellationToken ct = default) => SendPanelAsync(HttpMethod.Post, $"api/admin/panel-file-packages/{id:D}/archive", null, ct);
    public Task<PanelPackageDto?> RollbackPanelPackageAsync(Guid id, CancellationToken ct = default) => SendPanelAsync(HttpMethod.Post, $"api/admin/panel-file-packages/{id:D}/rollback", null, ct);
    public async Task DeletePanelPackageAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"api/admin/panel-file-packages/{id:D}", ct);
        if (!response.IsSuccessStatusCode) throw new LedSupBotApiException("Panel paketi silinemedi.", (int)response.StatusCode);
    }
    public async Task<PanelPackageDto?> UploadPanelAssetAsync(Guid id, string slot, string filePath, CancellationToken ct = default)
    {
        await using var file = File.OpenRead(filePath); using var content = new MultipartFormDataContent();
        using var part = new StreamContent(file); part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(part, "file", Path.GetFileName(filePath));
        using var response = await _http.PostAsync($"api/admin/panel-file-packages/{id:D}/assets/{slot}", content, ct);
        return await ReadPanelResponseAsync(response, ct);
    }
    public async Task<PanelPackageDto?> ImportPanelFolderAsync(string folderPath, CancellationToken ct = default)
    {
        MultipartFormDataContent content;
        try
        {
            content = PanelFolderImportMultipartFactory.Create(folderPath);
        }
        catch (InvalidDataException ex)
        {
            throw new LedSupBotApiException(ex.Message, inner: ex);
        }
        using (content)
        {
            try
            {
                using var response = await _http.PostAsync("api/admin/panel-file-packages/import-folder", content, ct);
                return await ReadPanelResponseAsync(response, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new LedSupBotApiException("API bağlantısı kurulamadı.", inner: ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new LedSupBotApiException("Klasör yükleme zaman aşımına uğradı.", inner: ex);
            }
        }
    }
    public async Task<PanelPackageDto?> ImportPanelFilesAsync(string folderName, IReadOnlyList<string> selectedFilePaths, CancellationToken ct = default)
    {
        MultipartFormDataContent content;
        try
        {
            content = PanelFolderImportMultipartFactory.Create(folderName, selectedFilePaths);
        }
        catch (InvalidDataException ex)
        {
            throw new LedSupBotApiException(ex.Message, inner: ex);
        }
        using (content)
        {
            try
            {
                using var response = await _http.PostAsync("api/admin/panel-file-packages/import-folder", content, ct);
                return await ReadPanelResponseAsync(response, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new LedSupBotApiException("API bağlantısı kurulamadı.", inner: ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new LedSupBotApiException("Seçili dosyaların yüklenmesi zaman aşımına uğradı.", inner: ex);
            }
        }
    }
    private async Task<PanelPackageDto?> SendPanelAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, ct); return await ReadPanelResponseAsync(response, ct);
    }
    private static async Task<PanelPackageDto?> ReadPanelResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            string? apiError = null;
            try { apiError = JsonDocument.Parse(raw).RootElement.GetProperty("error").GetString(); } catch (JsonException) { } catch (KeyNotFoundException) { }
            var status = (int)response.StatusCode;
            var message = status is 401 or 403 ? "Yetkilendirme başarısız. Yeniden giriş yapın."
                : apiError?.Contains("en az 2 dosya", StringComparison.OrdinalIgnoreCase) == true ? "Pakette en az 2 dosya olmalı."
                : apiError?.Contains("P+Chipset+Decoder", StringComparison.OrdinalIgnoreCase) == true ? "Bu klasör adı beklenen P+Chipset+Decoder formatında değil."
                : apiError?.Contains("size", StringComparison.OrdinalIgnoreCase) == true || apiError?.Contains("boyut", StringComparison.OrdinalIgnoreCase) == true ? "Dosya boyutu izin verilen sınırı aşıyor."
                : status == 409 ? "Bu paket içe aktarılırken kayıt çakışması oluştu."
                : apiError == "panel_folder_storage_failed" ? "Dosyalar özel depoya yazılamadı."
                : apiError == "panel_folder_persistence_failed" ? "Paket bilgileri veritabanına kaydedilemedi."
                : "Sunucu klasör yükleme işlemini tamamlayamadı.";
            throw new LedSupBotApiException(message, status);
        }
        return await response.Content.ReadFromJsonAsync<PanelPackageDto>(JsonOptions, ct);
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

    public async Task SaveTrainingFeedbackAsync(
        Guid messageId,
        SaveTrainingFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty)
        {
            throw new LedSupBotApiException("Geri bildirim için geçerli bir AI mesajı seçilmedi.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.PutAsJsonAsync(
                $"api/admin/training-feedback/{messageId:D}",
                request,
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LedSupBotApiException("API bağlantısı yok.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LedSupBotApiException("Eğitim geri bildirimi kaydedilirken zaman aşımı oluştu.", inner: ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var status = (int)response.StatusCode;
            var message = status switch
            {
                400 => "Eğitim geri bildirimi geçerli değil.",
                401 or 403 => "Eğitim geri bildirimi için yetkiniz yok.",
                404 => "Geri bildirim verilecek AI mesajı bulunamadı.",
                409 => "Bu mesaj eğitim geri bildirimi için uygun değil.",
                _ => "Eğitim geri bildirimi kaydedilemedi.",
            };
            throw new LedSupBotApiException(message, status);
        }
    }

    public async Task<TrainingExamplesResponse> GetTrainingExamplesAsync(
        string? status = null,
        string? intent = null,
        string? sourceDecision = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        AddQueryValue(query, "status", status);
        AddQueryValue(query, "intent", intent);
        AddQueryValue(query, "sourceDecision", sourceDecision);
        AddQueryValue(query, "dateFrom", dateFrom is { } from ? FormatLocalDateBoundary(from, endOfDay: false) : null);
        AddQueryValue(query, "dateTo", dateTo is { } to ? FormatLocalDateBoundary(to, endOfDay: true) : null);

        var path = "api/admin/training-examples";
        if (query.Count > 0)
        {
            path += "?" + string.Join("&", query);
        }

        return await GetAsync<TrainingExamplesResponse>(path, cancellationToken)
               ?? new TrainingExamplesResponse();
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

    private static void AddQueryValue(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            query.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    private static string FormatLocalDateBoundary(DateTime value, bool endOfDay)
    {
        var boundary = endOfDay
            ? value.Date.AddDays(1).AddTicks(-1)
            : value.Date;
        var localBoundary = DateTime.SpecifyKind(boundary, DateTimeKind.Unspecified);
        return new DateTimeOffset(localBoundary, TimeZoneInfo.Local.GetUtcOffset(localBoundary)).ToString("O");
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
