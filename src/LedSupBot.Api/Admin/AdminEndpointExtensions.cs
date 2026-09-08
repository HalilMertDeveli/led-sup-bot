using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.Training;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.CaseMemory;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.PanelFiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LedSupBot.Api.Admin;

public static class AdminEndpointExtensions
{
    public const string SessionCookieName = "led_admin_session";

    public static RouteGroupBuilder MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin");

        group.MapPost("/auth/login", LoginAsync);
        group.MapPost("/auth/logout", LogoutAsync);
        group.MapGet("/auth/status", AuthStatusAsync);

        group.MapGet("/system/status", SystemStatusAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/conversations", ListConversationsAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/conversations/{conversationId:guid}", GetConversationAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/conversations/{conversationId:guid}/messages", GetMessagesAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/conversations/{conversationId:guid}/messages", SendMessageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/conversations/{conversationId:guid}/takeover", TakeoverAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/conversations/{conversationId:guid}/pause", PauseAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/conversations/{conversationId:guid}/resume", ResumeAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/compact/focus", CompactFocusAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/conversations/{conversationId:guid}/dismiss-anydesk-alert", DismissAnyDeskAlertAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/media/{*storageKey}", ServeMediaAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPut("/training-feedback/{messageId:guid}", UpsertTrainingFeedbackAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/training-examples", ListTrainingExamplesAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/training-examples/{id:guid}", GetTrainingExampleAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/support-cases/{caseId:guid}/memory-draft", CreateMemoryDraftAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPut("/case-memories/{id:guid}", UpdateMemoryDraftAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/case-memories/{id:guid}/verify", VerifyMemoryAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/case-memories/{id:guid}/reject", RejectMemoryAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/case-memories/{id:guid}/archive", ArchiveMemoryAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/case-memories", ListMemoriesAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/panel-file-packages", ListPanelPackagesAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapGet("/panel-file-packages/{id:guid}", GetPanelPackageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages", CreatePanelPackageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapMethods("/panel-file-packages/{id:guid}", ["PUT", "PATCH"], UpdatePanelPackageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages/{id:guid}/activate", ActivatePanelPackageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages/{id:guid}/archive", ArchivePanelPackageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages/{id:guid}/rollback", RollbackPanelPackageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapDelete("/panel-file-packages/{id:guid}", DeletePanelPackageAsync).AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages/{id:guid}/assets/{slot}", UploadPanelAssetAsync)
            .DisableAntiforgery().AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages/import-folder", ImportPanelFolderAsync)
            .DisableAntiforgery().AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages/bulk-import/preview", BulkImportPreviewAsync)
            .AddEndpointFilter(AdminAuthFilter.InvokeAsync);
        group.MapPost("/panel-file-packages/bulk-import/run", BulkImportRunAsync)
            .AddEndpointFilter(AdminAuthFilter.InvokeAsync);

        return group;
    }

    private static IResult LoginAsync(
        [FromBody] LoginRequest request,
        [FromServices] IAdminAuthService auth,
        HttpContext http)
    {
        if (!auth.IsConfigured)
        {
            return Results.Problem(
                detail: "Admin dashboard authentication is not configured on the server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!auth.ValidatePassword(request.Password))
        {
            return Results.Unauthorized();
        }

        if (!auth.TryCreateSessionToken(out var token, out var expiresAt))
        {
            return Results.Problem("Unable to create admin session.");
        }

        http.Response.Cookies.Append(SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Path = "/",
        });

        return Results.Ok(new { ok = true, expiresAt });
    }

    private static IResult LogoutAsync(HttpContext http)
    {
        http.Response.Cookies.Delete(SessionCookieName);
        return Results.Ok(new { ok = true });
    }

    private static IResult AuthStatusAsync([FromServices] IAdminAuthService auth, HttpContext http)
    {
        var token = http.Request.Cookies[SessionCookieName];
        return Results.Ok(new
        {
            configured = auth.IsConfigured,
            authenticated = auth.TryValidateSessionToken(token),
        });
    }

    private static async Task<IResult> SystemStatusAsync(
        [FromServices] IWhatsAppConnectionStateStore state,
        [FromServices] IOptions<WorkflowOptions> workflow,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var whatsAppConnection = await WhatsAppLiveStatusResolver.GetCurrentAsync(services, state, cancellationToken);
        return Results.Ok(new SystemStatusDto(
            whatsAppConnection.ToString(),
            "connected",
            "configured",
            workflow.Value.AutoReplyEnabled));
    }

    private static async Task<IResult> ListConversationsAsync(
        [FromQuery] string? mode,
        [FromQuery] string? search,
        [FromServices] IAdminConversationService service,
        CancellationToken cancellationToken)
    {
        BotMode? filter = mode?.ToLowerInvariant() switch
        {
            "bot" => BotMode.Bot,
            "human" => BotMode.Human,
            "paused" => BotMode.Paused,
            _ => null,
        };

        var result = await service.ListAsync(filter, search, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetConversationAsync(
        Guid conversationId,
        [FromServices] IAdminConversationService service,
        CancellationToken cancellationToken)
    {
        var detail = await service.GetDetailAsync(conversationId, cancellationToken);
        return detail is null ? Results.NotFound(new { error = "conversation_not_found" }) : Results.Ok(detail);
    }

    private static async Task<IResult> GetMessagesAsync(
        Guid conversationId,
        [FromServices] IAdminConversationService service,
        CancellationToken cancellationToken)
    {
        var messages = await service.GetMessagesAsync(conversationId, cancellationToken);
        return messages is null ? Results.NotFound(new { error = "conversation_not_found" }) : Results.Ok(messages);
    }

    private static async Task<IResult> SendMessageAsync(
        Guid conversationId,
        [FromBody] SendMessageRequest request,
        [FromServices] IAdminTechnicianOutboundService outbound,
        CancellationToken cancellationToken)
    {
        var (success, error) = await outbound.SendTextAsync(conversationId, request.Text, cancellationToken);
        if (success is null)
        {
            return error switch
            {
                "conversation_not_found" => Results.NotFound(new { error }),
                "connector_unavailable" => Results.Problem(
                    detail: "WhatsApp connector unavailable or send failed.",
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.BadRequest(new { error }),
            };
        }

        return Results.Ok(success);
    }

    private static async Task<IResult> TakeoverAsync(
        Guid conversationId,
        [FromServices] IConversationTakeoverService takeover,
        [FromServices] IAdminEventNotifier notifier,
        CancellationToken cancellationToken)
    {
        var result = await takeover.TakeOverAsync(conversationId, TakeoverChangeSource.AdminTakeover, cancellationToken);
        if (!result.Found)
        {
            return Results.NotFound(new { error = "conversation_not_found" });
        }

        await notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);
        return Results.Ok(new TakeoverResponseDto(result.Changed, result.CurrentMode, result.CurrentStage, result.CurrentStatus));
    }

    private static async Task<IResult> PauseAsync(
        Guid conversationId,
        [FromServices] IConversationTakeoverService takeover,
        [FromServices] IAdminEventNotifier notifier,
        CancellationToken cancellationToken)
    {
        var result = await takeover.PauseAsync(conversationId, cancellationToken);
        if (!result.Found)
        {
            return Results.NotFound(new { error = "conversation_not_found" });
        }

        await notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);
        return Results.Ok(new TakeoverResponseDto(result.Changed, result.CurrentMode, result.CurrentStage, result.CurrentStatus));
    }

    private static async Task<IResult> ResumeAsync(
        Guid conversationId,
        [FromServices] IConversationTakeoverService takeover,
        [FromServices] IAdminEventNotifier notifier,
        CancellationToken cancellationToken)
    {
        var result = await takeover.ResumeBotAsync(conversationId, cancellationToken);
        if (!result.Found)
        {
            return Results.NotFound(new { error = "conversation_not_found" });
        }

        await notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);
        return Results.Ok(new TakeoverResponseDto(result.Changed, result.CurrentMode, result.CurrentStage, result.CurrentStatus));
    }

    private static async Task<IResult> CompactFocusAsync(
        [FromServices] IAdminConversationService admin,
        CancellationToken cancellationToken)
    {
        var focus = await admin.GetCompactFocusAsync(cancellationToken);
        return Results.Ok(focus);
    }

    private static async Task<IResult> DismissAnyDeskAlertAsync(
        Guid conversationId,
        [FromServices] IAdminConversationService admin,
        [FromServices] IAdminEventNotifier notifier,
        CancellationToken cancellationToken)
    {
        var ok = await admin.DismissAnyDeskAlertAsync(conversationId, cancellationToken);
        if (ok)
        {
            await notifier.NotifyConversationUpdatedAsync(conversationId, cancellationToken);
        }
        return ok ? Results.Ok(new { ok = true }) : Results.NotFound();
    }

    private static async Task<IResult> ServeMediaAsync(
        string storageKey,
        [FromServices] IMediaStorage storage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storageKey) ||
            storageKey.Contains("..", StringComparison.Ordinal) ||
            storageKey.StartsWith('/') ||
            storageKey.StartsWith('\\'))
        {
            return Results.BadRequest(new { error = "invalid_storage_key" });
        }

        if (!storage.Exists(storageKey))
        {
            return Results.NotFound(new { error = "media_not_found" });
        }

        var stream = await storage.OpenReadAsync(storageKey, cancellationToken);
        var contentType = GuessContentType(storageKey);
        return Results.File(stream, contentType);
    }

    private static async Task<IResult> UpsertTrainingFeedbackAsync(
        Guid messageId,
        [FromBody] AiTrainingFeedbackRequest request,
        [FromServices] IAiTrainingService training,
        CancellationToken cancellationToken)
    {
        var result = await training.UpsertFeedbackAsync(messageId, request, cancellationToken);
        if (result.Success)
        {
            return Results.Ok(result.Example);
        }

        return result.Error == "message_not_training_eligible"
            ? Results.Conflict(new { error = result.Error })
            : Results.BadRequest(new { error = result.Error });
    }

    private static async Task<IResult> ListTrainingExamplesAsync(
        [FromQuery] AiTrainingFeedbackStatus? status,
        [FromQuery] string? intent,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] AiTrainingSourceDecision? sourceDecision,
        [FromQuery] int? limit,
        [FromServices] IAiTrainingService training,
        CancellationToken cancellationToken)
    {
        var query = new AiTrainingListQuery(
            status,
            intent,
            dateFrom,
            dateTo,
            sourceDecision,
            limit is null or <= 0 ? 200 : limit.Value);
        return Results.Ok(await training.ListAsync(query, cancellationToken));
    }

    private static async Task<IResult> GetTrainingExampleAsync(
        Guid id,
        [FromServices] IAiTrainingService training,
        CancellationToken cancellationToken)
    {
        var result = await training.GetDetailAsync(id, cancellationToken);
        return result is null
            ? Results.NotFound(new { error = "training_example_not_found" })
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateMemoryDraftAsync(Guid caseId, [FromBody] CaseMemoryEdit? edit,
        [FromServices] IVerifiedCaseMemoryService service, CancellationToken ct) =>
        await service.CreateDraftAsync(caseId, edit, ct) is { } memory ? Results.Ok(memory) : Results.NotFound();

    private static async Task<IResult> UpdateMemoryDraftAsync(Guid id, [FromBody] CaseMemoryEdit edit,
        [FromServices] IVerifiedCaseMemoryService service, CancellationToken ct) =>
        await service.UpdateDraftAsync(id, edit, ct) is { } memory ? Results.Ok(memory) : Results.NotFound();

    private static async Task<IResult> VerifyMemoryAsync(Guid id, [FromServices] IVerifiedCaseMemoryService service, CancellationToken ct) =>
        await service.VerifyAsync(id, "desktop-operator", ct) is { } memory ? Results.Ok(memory) : Results.BadRequest();

    private static async Task<IResult> RejectMemoryAsync(Guid id, [FromServices] IVerifiedCaseMemoryService service, CancellationToken ct) =>
        await service.SetStatusAsync(id, VerifiedCaseMemoryStatus.Rejected, ct) is { } memory ? Results.Ok(memory) : Results.NotFound();

    private static async Task<IResult> ArchiveMemoryAsync(Guid id, [FromServices] IVerifiedCaseMemoryService service, CancellationToken ct) =>
        await service.SetStatusAsync(id, VerifiedCaseMemoryStatus.Archived, ct) is { } memory ? Results.Ok(memory) : Results.NotFound();

    private static async Task<IResult> ListMemoriesAsync([FromQuery] VerifiedCaseMemoryStatus? status, [FromQuery] string? model,
        [FromQuery] string? category, [FromServices] IVerifiedCaseMemoryService service, CancellationToken ct) =>
        Results.Ok(await service.ListAsync(status, model, category, ct));

    private static async Task<IResult> ListPanelPackagesAsync([FromQuery] string? search, [FromQuery] string? pitch,
        [FromQuery] string? code, [FromQuery] PanelFilePackageStatus? status, [FromQuery] string? readiness,
        [FromQuery] int? limit, [FromServices] IPanelFilePackageAdminQuery query, CancellationToken ct) =>
        Results.Ok(await query.ListAsync(search, pitch, code, status, readiness, limit ?? 100, ct));

    private static async Task<IResult> GetPanelPackageAsync(Guid id, [FromServices] IPanelFilePackageAdminQuery query, CancellationToken ct) =>
        await query.GetAsync(id, ct) is { } package ? Results.Ok(package) : Results.NotFound(new { error = "package_not_found" });

    private static async Task<IResult> CreatePanelPackageAsync([FromBody] PanelPackageWriteRequest request,
        [FromServices] IPanelFilePackageRegistry registry, [FromServices] IPanelFilePackageAdminQuery query,
        [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        try
        {
            var package = await registry.CreateDraftAsync(request.PixelPitch, request.PanelCode, request.SecondaryCode, request.DisplayName, ct);
            if (request.Version > 1 || request.Manufacturer is not null || request.Description is not null || request.Notes is not null)
                package = await registry.UpdateDraftAsync(package.Id, package.PixelPitch, package.PanelCode, package.SecondaryCode,
                    request.DisplayName, request.Manufacturer, request.Description, request.Notes, package.Version, ct) ?? package;
            await notifier.NotifyPanelPackagesChangedAsync(package.Id, "created", ct);
            return Results.Created($"/api/admin/panel-file-packages/{package.Id}", await query.GetAsync(package.Id, ct));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException) { return Results.Conflict(new { error = "Bu paket zaten mevcut." }); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "Bu paket zaten mevcut." }); }
    }

    private static async Task<IResult> UpdatePanelPackageAsync(Guid id, [FromBody] PanelPackageWriteRequest request,
        [FromServices] IPanelFilePackageRegistry registry, [FromServices] IPanelFilePackageAdminQuery query,
        [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        try
        {
            var updated = await registry.UpdateDraftAsync(id, request.PixelPitch, request.PanelCode, request.SecondaryCode,
                request.DisplayName, request.Manufacturer, request.Description, request.Notes, request.Version, ct);
            if (updated is null) return Results.Conflict(new { error = "Paket düzenlenemedi; sürüm değişmiş veya paket arşivlenmiş olabilir." });
            await notifier.NotifyPanelPackagesChangedAsync(id, "updated", ct);
            return Results.Ok(await query.GetAsync(id, ct));
        }
        catch (InvalidOperationException) { return Results.Conflict(new { error = "Bu paket zaten mevcut." }); }
    }

    private static async Task<IResult> ActivatePanelPackageAsync(Guid id, [FromServices] IPanelFilePackageRegistry registry,
        [FromServices] IPanelFilePackageAdminQuery query, [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        if (!await registry.ActivateAsync(id, ct)) return Results.Conflict(new { error = ".hex ve .rcvbp dosyaları tamamlanmadan paket aktifleştirilemez." });
        await notifier.NotifyPanelPackagesChangedAsync(id, "activated", ct);
        return Results.Ok(await query.GetAsync(id, ct));
    }

    private static async Task<IResult> ArchivePanelPackageAsync(Guid id, [FromServices] IPanelFilePackageRegistry registry,
        [FromServices] IPanelFilePackageAdminQuery query, [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        if (!await registry.ArchiveAsync(id, ct)) return Results.NotFound();
        await notifier.NotifyPanelPackagesChangedAsync(id, "archived", ct);
        return Results.Ok(await query.GetAsync(id, ct));
    }

    private static async Task<IResult> RollbackPanelPackageAsync(Guid id, [FromServices] IPanelFilePackageRegistry registry,
        [FromServices] IPanelFilePackageAdminQuery query, [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        if (await registry.RollbackAsync(id, ct) is null)
            return Results.Conflict(new { error = "Önceki geçerli sürüm bulunmuyor." });
        await notifier.NotifyPanelPackagesChangedAsync(id, "rolled-back", ct);
        return Results.Ok(await query.GetAsync(id, ct));
    }

    private static async Task<IResult> DeletePanelPackageAsync(Guid id, [FromServices] IPanelFilePackageRegistry registry,
        [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        if (!await registry.ArchiveAsync(id, ct)) return Results.NotFound(new { error = "package_not_found" });
        await notifier.NotifyPanelPackagesChangedAsync(id, "deleted", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UploadPanelAssetAsync(Guid id, string slot, IFormFile file,
        [FromServices] IPanelFileAssetRegistry registry, [FromServices] IPanelFilePackageAdminQuery query,
        [FromServices] IOptions<PanelFilePackageOptions> options, [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        var type = slot.ToLowerInvariant() switch { "firmware" => PanelFileAssetType.FirmwareHex, "receiver-config" => PanelFileAssetType.ReceiverConfigRcvbp, _ => (PanelFileAssetType?)null };
        if (type is null) return Results.BadRequest(new { error = "Geçersiz dosya yuvası." });
        var max = type == PanelFileAssetType.FirmwareHex ? options.Value.MaxFirmwareFileSizeBytes : options.Value.MaxReceiverConfigFileSizeBytes;
        try
        {
            await using var stream = file.OpenReadStream();
            await registry.StoreAsync(id, type.Value, file.FileName, stream, max, ct);
            await notifier.NotifyPanelPackagesChangedAsync(id, "asset-updated", ct);
            return Results.Ok(await query.GetAsync(id, ct));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException) { return Results.Conflict(new { error = "Bu dosya zaten bu pakette mevcut." }); }
        catch (KeyNotFoundException) { return Results.NotFound(new { error = "package_not_found" }); }
        catch (IOException) { return Results.Json(new { error = "panel_file_storage_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (DbUpdateException) { return Results.Json(new { error = "panel_file_persistence_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static async Task<IResult> BulkImportPreviewAsync([FromBody] PanelBulkImportRequest request,
        [FromServices] IPanelFilePackageAdminQuery query, CancellationToken ct)
    {
        try
        {
            var existing = await LoadExistingCanonicalKeysAsync(query, ct);
            var items = PanelBulkFolderDiscovery.Discover(request.ParentFolder, existing);
            return Results.Ok(new { items });
        }
        catch (DirectoryNotFoundException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden); }
    }

    private static async Task<IResult> BulkImportRunAsync([FromBody] PanelBulkImportRequest request,
        [FromServices] IPanelBulkFolderImportService bulkImporter, [FromServices] IPanelFilePackageAdminQuery query,
        [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        try
        {
            var result = await bulkImporter.ImportAllAsync(request.ParentFolder, ct);
            var refreshed = await query.ListAsync(null, null, null, null, null, 1_000_000, ct);
            foreach (var item in result.Results)
            {
                if (item.Status != "Imported" || item.CanonicalKey is null)
                {
                    continue;
                }

                var found = refreshed.Items.FirstOrDefault(x => x.CanonicalKey is not null &&
                    string.Equals(x.CanonicalKey, item.CanonicalKey, StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                {
                    await notifier.NotifyPanelPackagesChangedAsync(found.Id, "imported", ct);
                }
            }

            return Results.Ok(result);
        }
        catch (DirectoryNotFoundException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden); }
    }

    private static async Task<HashSet<string>> LoadExistingCanonicalKeysAsync(IPanelFilePackageAdminQuery query, CancellationToken ct)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = await query.ListAsync(null, null, null, null, null, 1_000_000, ct);
        foreach (var item in list.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.CanonicalKey))
            {
                existing.Add(item.CanonicalKey);
            }
        }

        return existing;
    }

    private static async Task<IResult> ImportPanelFolderAsync(HttpRequest request,
        [FromServices] IPanelFolderImportService importer, [FromServices] IPanelFilePackageAdminQuery query,
        [FromServices] IAdminEventNotifier notifier, CancellationToken ct)
    {
        try
        {
            var form = await request.ReadFormAsync(ct);
            var folderName = form["folderName"].ToString();
            var files = form.Files.Select(file => new PanelFolderImportFile(
                file.FileName.Replace('\\', '/'), Path.GetFileName(file.FileName),
                file.ContentType, file.OpenReadStream())).ToList();
            try
            {
                var package = await importer.ImportAsync(folderName, files, ct);
                await notifier.NotifyPanelPackagesChangedAsync(package.Id, "imported", ct);
                return Results.Ok(await query.GetAsync(package.Id, ct));
            }
            finally
            {
                foreach (var file in files) await file.Content.DisposeAsync();
            }
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (IOException) { return Results.Json(new { error = "panel_folder_storage_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (DbUpdateException) { return Results.Json(new { error = "panel_folder_persistence_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (NpgsqlException) { return Results.Json(new { error = "panel_folder_persistence_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (UnauthorizedAccessException) { return Results.Json(new { error = "panel_folder_storage_failed" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static string GuessContentType(string storageKey)
    {
        var ext = Path.GetExtension(storageKey).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }

    internal sealed record LoginRequest(string Password);
    internal sealed record PanelBulkImportRequest(string ParentFolder);
    internal sealed record PanelPackageWriteRequest(string PixelPitch, string PanelCode, string SecondaryCode,
        string? DisplayName, string? Manufacturer, string? Description, string? Notes, int Version = 1);
}

internal static class AdminAuthFilter
{
    public static ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var auth = context.HttpContext.RequestServices.GetRequiredService<IAdminAuthService>();
        if (!auth.IsConfigured)
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                detail: "Admin dashboard authentication is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var token = context.HttpContext.Request.Cookies[AdminEndpointExtensions.SessionCookieName];
        if (!auth.TryValidateSessionToken(token))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        return next(context);
    }
}
