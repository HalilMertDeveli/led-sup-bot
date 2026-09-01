using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Media;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Media;

public sealed class InboundMediaPipeline : IInboundMediaPipeline
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly IMediaStorage _mediaStorage;
    private readonly IMediaImageValidator _validator;
    private readonly MediaStorageOptions _options;
    private readonly ILogger<InboundMediaPipeline> _logger;

    public InboundMediaPipeline(
        IApplicationDbContext db,
        IWhatsAppClient whatsAppClient,
        IMediaStorage mediaStorage,
        IMediaImageValidator validator,
        IOptions<MediaStorageOptions> options,
        ILogger<InboundMediaPipeline> logger)
    {
        _db = db;
        _whatsAppClient = whatsAppClient;
        _mediaStorage = mediaStorage;
        _validator = validator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InboundMediaProcessResult> ProcessInboundImageAsync(
        Message message,
        TechnicalSupportCase supportCase,
        WhatsAppInboundEvent inboundEvent,
        CancellationToken cancellationToken = default)
    {
        if (message.MessageType != MessageType.Image)
        {
            return new InboundMediaProcessResult(MediaProcessOutcome.SkippedNonCustomer);
        }

        if (!string.IsNullOrWhiteSpace(message.StorageKey))
        {
            var existingPhoto = await _db.PanelLabelPhotos
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    p => p.TechnicalSupportCaseId == supportCase.Id && p.StorageKey == message.StorageKey,
                    cancellationToken);

            return new InboundMediaProcessResult(
                MediaProcessOutcome.AlreadyProcessed,
                message.StorageKey,
                existingPhoto?.ContentType,
                existingPhoto?.SizeBytes,
                existingPhoto?.Id);
        }

        if (inboundEvent.Media?.Downloadable != true)
        {
            _logger.LogWarning(
                "Inbound image not downloadable for message {ExternalMessageId}",
                inboundEvent.ExternalMessageId);
            return new InboundMediaProcessResult(MediaProcessOutcome.NotDownloadable);
        }

        if (inboundEvent.Media.FileLength is > 0 &&
            inboundEvent.Media.FileLength > _options.MaxFileSizeBytes)
        {
            _logger.LogWarning(
                "Inbound image exceeds size limit for message {ExternalMessageId}",
                inboundEvent.ExternalMessageId);
            return new InboundMediaProcessResult(MediaProcessOutcome.FileTooLarge);
        }

        WhatsAppMediaDownloadResult download;
        try
        {
            download = await _whatsAppClient.DownloadMediaAsync(
                inboundEvent.ExternalMessageId!,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Media download failed for message {ExternalMessageId}",
                inboundEvent.ExternalMessageId);
            return new InboundMediaProcessResult(MediaProcessOutcome.DownloadFailed);
        }

        if (download.Content.Length == 0)
        {
            return new InboundMediaProcessResult(MediaProcessOutcome.InvalidImage);
        }

        if (download.Content.LongLength > _options.MaxFileSizeBytes)
        {
            return new InboundMediaProcessResult(MediaProcessOutcome.FileTooLarge);
        }

        var validation = _validator.Validate(download.Content, download.MimeType ?? inboundEvent.Media.MimeType);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Inbound image validation failed for message {ExternalMessageId} reason {Reason}",
                inboundEvent.ExternalMessageId,
                validation.FailureReason);
            return validation.FailureReason == "unsupported_mime_type"
                ? new InboundMediaProcessResult(MediaProcessOutcome.UnsupportedMimeType)
                : new InboundMediaProcessResult(MediaProcessOutcome.InvalidImage);
        }

        var contentType = validation.NormalizedContentType!;
        var extension = MapExtension(contentType);
        var keyPrefix = $"cases/{supportCase.Id:N}";

        StoredMediaReference stored;
        try
        {
            await using var stream = new MemoryStream(download.Content, writable: false);
            stored = await _mediaStorage.StoreAsync(
                stream,
                contentType,
                new MediaStoreRequest(keyPrefix, extension),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Media storage failed for message {ExternalMessageId}",
                inboundEvent.ExternalMessageId);
            return new InboundMediaProcessResult(MediaProcessOutcome.StorageFailed);
        }

        var trackedMessage = await _db.Messages.SingleAsync(m => m.Id == message.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(trackedMessage.StorageKey))
        {
            return new InboundMediaProcessResult(
                MediaProcessOutcome.AlreadyProcessed,
                trackedMessage.StorageKey);
        }

        var duplicatePhoto = await _db.PanelLabelPhotos
            .AnyAsync(p => p.StorageKey == stored.StorageKey, cancellationToken);
        if (duplicatePhoto)
        {
            trackedMessage.StorageKey = stored.StorageKey;
            await _db.SaveChangesAsync(cancellationToken);
            return new InboundMediaProcessResult(
                MediaProcessOutcome.AlreadyProcessed,
                stored.StorageKey,
                stored.ContentType,
                stored.SizeBytes);
        }

        var sortOrder = await _db.PanelLabelPhotos.CountAsync(
            p => p.TechnicalSupportCaseId == supportCase.Id,
            cancellationToken);

        var photo = new PanelLabelPhoto
        {
            TechnicalSupportCaseId = supportCase.Id,
            StorageKey = stored.StorageKey,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            SortOrder = sortOrder + 1,
            AnalysisStatus = MediaAnalysisStatus.NotStarted,
        };

        _db.PanelLabelPhotos.Add(photo);
        trackedMessage.StorageKey = stored.StorageKey;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored inbound panel photo for message {ExternalMessageId} size {SizeBytes}",
            inboundEvent.ExternalMessageId,
            stored.SizeBytes);

        return new InboundMediaProcessResult(
            MediaProcessOutcome.Stored,
            stored.StorageKey,
            stored.ContentType,
            stored.SizeBytes,
            photo.Id);
    }

    private static string MapExtension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin",
    };
}
