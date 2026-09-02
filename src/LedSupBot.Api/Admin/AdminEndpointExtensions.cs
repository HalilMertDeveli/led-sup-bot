using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Admin;
using LedSupBot.Application.Takeover;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Application.Workflow;
using LedSupBot.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
        CancellationToken cancellationToken)
    {
        var ok = await admin.DismissAnyDeskAlertAsync(conversationId, cancellationToken);
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
