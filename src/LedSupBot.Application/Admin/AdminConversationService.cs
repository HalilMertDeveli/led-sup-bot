using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Workflow;
using LedSupBot.Application.Workflow.Parsing;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Application.Admin;

public interface IAdminConversationService
{
    Task<ConversationListResponse> ListAsync(BotMode? modeFilter, string? search, CancellationToken cancellationToken = default);

    Task<ConversationDetailDto?> GetDetailAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<MessagesResponse?> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<CompactFocusDto> GetCompactFocusAsync(CancellationToken cancellationToken = default);

    Task<bool> DismissAnyDeskAlertAsync(Guid conversationId, CancellationToken cancellationToken = default);
}

public sealed class AdminConversationService : IAdminConversationService
{
    private readonly IApplicationDbContext _db;
    private readonly IConversationReplyCoordinator? _replyCoordinator;

    public AdminConversationService(
        IApplicationDbContext db,
        IConversationReplyCoordinator? replyCoordinator = null)
    {
        _db = db;
        _replyCoordinator = replyCoordinator;
    }

    public async Task<ConversationListResponse> ListAsync(
        BotMode? modeFilter,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Conversations
            .AsNoTracking()
            .Include(c => c.Customer)
            .Where(c => c.Status != ConversationStatus.Abandoned);

        if (modeFilter.HasValue)
        {
            query = query.Where(c => c.BotMode == modeFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var termLower = search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                (c.Customer.DisplayName != null && c.Customer.DisplayName.ToLower().Contains(termLower)) ||
                c.Customer.PhoneNumber.ToLower().Contains(termLower));
        }

        var conversations = await query
            .OrderByDescending(c => c.LastCustomerMessageAt ?? c.UpdatedAt)
            .ThenByDescending(c => c.StartedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (conversations.Count == 0)
        {
            return new ConversationListResponse([], 0);
        }

        var ids = conversations.Select(c => c.Id).ToList();
        var lastMessages = await _db.Messages
            .AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
            .ToListAsync(cancellationToken);

        var lastByConversation = lastMessages.ToDictionary(m => m.ConversationId);
        var counts = await _db.Messages
            .AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, cancellationToken);

        var items = conversations.Select(c =>
        {
            lastByConversation.TryGetValue(c.Id, out var last);
            counts.TryGetValue(c.Id, out var count);
            return MapListItem(c, c.Customer, last, count);
        }).ToList();

        return new ConversationListResponse(items, items.Count);
    }

    public async Task<ConversationDetailDto?> GetDetailAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Customer)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.ScreenInformation)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.ReceiverInformation)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.ConfigConnection)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.AnyDeskInformation)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.PanelLabelPhotos)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        var activeCase = conversation.TechnicalSupportCases
            .Where(tc => tc.Status != SupportCaseStatus.Completed && tc.Status != SupportCaseStatus.Cancelled)
            .OrderByDescending(tc => tc.CreatedAt)
            .FirstOrDefault()
            ?? conversation.TechnicalSupportCases.OrderByDescending(tc => tc.CreatedAt).FirstOrDefault();

        var completedCount = conversation.TechnicalSupportCases.Count(tc => tc.Status == SupportCaseStatus.Completed);

        var audits = await _db.BotModeChangeAudits
            .AsNoTracking()
            .Where(a => a.ConversationId == conversationId)
            .OrderByDescending(a => a.ChangedAt)
            .Take(10)
            .Select(a => new BotModeAuditDto(a.PreviousMode, a.NewMode, a.Source, a.ChangedAt))
            .ToListAsync(cancellationToken);

        return new ConversationDetailDto(
            conversation.Id,
            conversation.Status,
            conversation.BotMode,
            conversation.CurrentStage,
            AdminStageLabels.ToLabel(conversation.CurrentStage),
            conversation.StartedAt,
            conversation.ClosedAt,
            conversation.LastCustomerMessageAt,
            new CustomerSummaryDto(
                conversation.Customer.Id,
                AdminDisplayFormat.MaskPhone(conversation.Customer.PhoneNumber),
                conversation.Customer.DisplayName),
            activeCase is null ? null : MapCase(activeCase, completedCount),
            audits,
            BuildBotReplyStatusLabel(conversation));
    }

    private string? BuildBotReplyStatusLabel(Conversation conversation)
    {
        if (conversation.BotMode == BotMode.Paused || conversation.BotMode == BotMode.Human)
        {
            return "Durduruldu";
        }

        var activity = _replyCoordinator?.GetActivity(conversation.Id) ?? BotReplyActivity.Idle;
        return activity switch
        {
            BotReplyActivity.Preparing => "Yanıt hazırlanıyor…",
            BotReplyActivity.Typing => "Yazıyor…",
            _ => "Bot aktif",
        };
    }

    public async Task<MessagesResponse?> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var exists = await _db.Conversations.AnyAsync(c => c.Id == conversationId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto(
                m.Id,
                m.Direction,
                m.MessageType,
                m.Content,
                m.SentBy,
                m.DeliveryStatus,
                m.CreatedAt,
                m.StorageKey,
                m.StorageKey != null ? $"/api/admin/media/{Uri.EscapeDataString(m.StorageKey)}" : null))
            .ToListAsync(cancellationToken);

        return new MessagesResponse(messages);
    }

    private static ConversationListItemDto MapListItem(
        Conversation conversation,
        Customer customer,
        Message? lastMessage,
        int messageCount)
    {
        string? preview = null;
        if (lastMessage is not null)
        {
            preview = lastMessage.MessageType == MessageType.Image
                ? "📷 Fotoğraf"
                : !string.IsNullOrWhiteSpace(lastMessage.Content)
                    ? Truncate(lastMessage.Content, 80)
                    : "(medya)";
        }

        var updatedAt = lastMessage?.CreatedAt ?? conversation.LastCustomerMessageAt ?? conversation.UpdatedAt;

        return new ConversationListItemDto(
            conversation.Id,
            AdminDisplayFormat.FormatCustomerIdentifier(customer),
            customer.DisplayName,
            conversation.Status,
            conversation.BotMode,
            conversation.CurrentStage,
            AdminStageLabels.ToLabel(conversation.CurrentStage),
            preview,
            conversation.LastCustomerMessageAt,
            updatedAt,
            messageCount,
            RequiresAttention(conversation, lastMessage));
    }

    private static bool RequiresAttention(Conversation conversation, Message? lastMessage)
    {
        if (conversation.BotMode is BotMode.Human or BotMode.Paused)
        {
            return true;
        }

        if (conversation.CurrentStage == ConversationStage.HumanHandoff)
        {
            return true;
        }

        if (conversation.Status == ConversationStatus.WaitingTechnician)
        {
            return true;
        }

        return lastMessage is { SentBy: MessageSender.Customer, Direction: MessageDirection.Inbound }
               && conversation.BotMode == BotMode.Bot;
    }

    private static CaseDetailDto MapCase(TechnicalSupportCase supportCase, int completedCasesCount = 0)
    {
        ScreenDetailDto? screen = null;
        if (supportCase.ScreenInformation is { } s)
        {
            screen = new ScreenDetailDto(s.Width, s.Height, $"{s.Width:0} × {s.Height:0} cm");
        }

        ReceiverDetailDto? receiver = null;
        if (supportCase.ReceiverInformation is { } r)
        {
            receiver = new ReceiverDetailDto(r.Count);
        }

        ConfigDetailDto? config = null;
        if (supportCase.ConfigConnection is { } cfg)
        {
            config = new ConfigDetailDto(cfg.Status, cfg.ExampleImageSent, AdminDisplayFormat.ConfigStatus(cfg.Status));
        }

        AnyDeskDetailDto? anyDesk = null;
        if (supportCase.AnyDeskInformation is { } ad && !string.IsNullOrWhiteSpace(ad.AnyDeskAddress))
        {
            anyDesk = new AnyDeskDetailDto(AdminDisplayFormat.MaskAnyDesk(ad.AnyDeskAddress));
        }

        var photos = supportCase.PanelLabelPhotos
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.CreatedAt)
            .Select(p => new PhotoDetailDto(
                p.Id,
                p.StorageKey,
                p.ContentType,
                p.SizeBytes,
                p.SortOrder,
                p.AnalysisStatus,
                $"/api/admin/media/{Uri.EscapeDataString(p.StorageKey)}"))
            .ToList();

        return new CaseDetailDto(
            supportCase.Id,
            supportCase.Status,
            supportCase.SummaryMarkdown,
            supportCase.IssueSummary,
            supportCase.AiOperatorSummary,
            screen,
            receiver,
            config,
            anyDesk,
            photos,
            completedCasesCount,
            supportCase.PlayerType,
            supportCase.ProcessorHdmiConnected);
    }

    public async Task<CompactFocusDto> GetCompactFocusAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Customer)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.ScreenInformation)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.ReceiverInformation)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.ConfigConnection)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.AnyDeskInformation)
            .Include(c => c.TechnicalSupportCases)
                .ThenInclude(tc => tc.PanelLabelPhotos)
            .Where(c => c.Status != ConversationStatus.Completed && c.Status != ConversationStatus.Abandoned)
            .OrderByDescending(c => c.LastCustomerMessageAt ?? c.UpdatedAt)
            .Take(40)
            .ToListAsync(cancellationToken);

        var conversation = candidates
            .OrderByDescending(HasPendingAnyDeskAlert)
            .ThenByDescending(c => c.LastCustomerMessageAt ?? c.UpdatedAt)
            .FirstOrDefault();

        if (conversation is null)
        {
            return new CompactFocusDto(
                null, null, null, null, null, null, null, null, null, null, "—", "—", "—", 0, false, [], 0);
        }

        var activeCase = conversation.TechnicalSupportCases
            .Where(tc => tc.Status != SupportCaseStatus.Completed && tc.Status != SupportCaseStatus.Cancelled)
            .OrderByDescending(tc => tc.CreatedAt)
            .FirstOrDefault()
            ?? conversation.TechnicalSupportCases.OrderByDescending(tc => tc.CreatedAt).FirstOrDefault();

        var completedCount = conversation.TechnicalSupportCases.Count(tc => tc.Status == SupportCaseStatus.Completed);
        var slots = WorkflowSlotState.FromCase(activeCase, conversation);

        var recent = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(6)
            .ToListAsync(cancellationToken);
        recent.Reverse();

        var recentDtos = recent.Select(m => new CompactRecentMessageDto(
            m.SentBy == MessageSender.Customer ? "customer" :
            m.SentBy == MessageSender.Technician ? "technician" :
            m.SentBy == MessageSender.Bot ? "bot" : "system",
            m.MessageType == MessageType.Image ? "📷 Fotoğraf" : Truncate(m.Content ?? "—", 120),
            m.CreatedAt)).ToList();

        var receiverSummary = activeCase?.ReceiverInformation is { Count: > 0 } r
            ? string.Join(" ", new[] { r.Manufacturer, r.Model, $"×{r.Count}" }.Where(x => !string.IsNullOrWhiteSpace(x)))
            : null;

        var showAlert = activeCase?.AnyDeskAlertSentAt is not null &&
                        activeCase.AnyDeskAlertDismissedAt is null &&
                        activeCase.AnyDeskInformation is null;

        var photoCount = activeCase?.PanelLabelPhotos?.Count ?? 0;

        return new CompactFocusDto(
            conversation.Id,
            conversation.BotMode,
            BuildBotReplyStatusLabel(conversation),
            AdminDisplayFormat.FormatCustomerIdentifier(conversation.Customer),
            activeCase?.IssueSummary,
            activeCase?.AiOperatorSummary,
            conversation.CurrentStage,
            AdminStageLabels.ToLabel(conversation.CurrentStage),
            slots.IsScreenComplete ? $"{slots.WidthCm:0}×{slots.HeightCm:0} cm" : null,
            receiverSummary,
            slots.HasPanelLabelPhoto ? "alındı" : "bekleniyor",
            slots.ConfigConfirmed ? "tamam" : "bekleniyor",
            slots.HasAnyDesk ? "alındı" : "bekleniyor",
            photoCount,
            showAlert,
            recentDtos,
            completedCount);
    }

    public async Task<bool> DismissAnyDeskAlertAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var updated = await _db.TechnicalSupportCases
            .Where(c => c.ConversationId == conversationId && c.Status != SupportCaseStatus.Completed)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.AnyDeskAlertDismissedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        return updated > 0;
    }

    private static bool HasPendingAnyDeskAlert(Conversation conversation)
    {
        var activeCase = conversation.TechnicalSupportCases
            .FirstOrDefault(tc => tc.Status != SupportCaseStatus.Completed && tc.Status != SupportCaseStatus.Cancelled);
        return activeCase?.AnyDeskAlertSentAt is not null &&
               activeCase.AnyDeskAlertDismissedAt is null &&
               activeCase.AnyDeskInformation is null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}

public static class AdminDisplayFormat
{
    public static string FormatCustomerIdentifier(Customer customer) =>
        !string.IsNullOrWhiteSpace(customer.DisplayName)
            ? customer.DisplayName
            : MaskPhone(customer.PhoneNumber);

    public static string MaskPhone(string phone) => string.IsNullOrWhiteSpace(phone) ? "—" : phone;

    public static string MaskAnyDesk(string address) => string.IsNullOrWhiteSpace(address) ? "—" : address;

    public static string ConfigStatus(ConfigConnectionStatus status) => status switch
    {
        ConfigConnectionStatus.Confirmed => "Bağlandı",
        ConfigConnectionStatus.Requested => "Bekleniyor",
        ConfigConnectionStatus.Failed => "Bağlanamadı",
        ConfigConnectionStatus.NotStarted => "Bilinmiyor",
        _ => status.ToString(),
    };
}
