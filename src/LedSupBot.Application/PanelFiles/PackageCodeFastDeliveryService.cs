using LedSupBot.Application.Abstractions;
using LedSupBot.Application.WhatsApp;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.Enums;
using LedSupBot.Domain.PanelFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LedSupBot.Application.PanelFiles;

public interface IPackageCodeFastDeliveryService
{
    Task<bool> TryDeliverAsync(Guid conversationId, Message inboundMessage, Customer customer,
        string customerIdentity, string? deliveryRoute, CancellationToken cancellationToken = default);
}

/// <summary>Exact package-code route that runs before conversation/AI processing.</summary>
public sealed class PackageCodeFastDeliveryService(IServiceProvider services, ILogger<PackageCodeFastDeliveryService> logger)
    : IPackageCodeFastDeliveryService
{
    public async Task<bool> TryDeliverAsync(Guid conversationId, Message inboundMessage, Customer customer,
        string customerIdentity, string? deliveryRoute, CancellationToken cancellationToken = default)
    {
        if (!TryParseExactCode(inboundMessage.Content, out var identity)) return false;
        var db = services.GetRequiredService<IApplicationDbContext>();
        var packages = services.GetRequiredService<IPanelFilePackageResolver>();
        var storage = services.GetRequiredService<IMediaStorage>();
        var client = services.GetRequiredService<IWhatsAppClient>();
        var allowlist = services.GetRequiredService<IWhatsAppTestAllowlist>();
        var selfIdentity = services.GetRequiredService<IWhatsAppSelfIdentityStore>();
        var resolution = await packages.ResolveAsync(identity, cancellationToken);
        if (resolution.Status == PanelFilePackageResolutionStatus.NoMatch) return false;
        if (resolution.Status != PanelFilePackageResolutionStatus.ExactMatch || resolution.PackageId is null ||
            resolution.Assets is null || resolution.Assets.Count == 0) return false;

        var destination = deliveryRoute ?? customerIdentity;
        if (WhatsAppEventMapper.IsGroupJid(destination) || selfIdentity.IsSelfJid(destination) ||
            !allowlist.IsOutboundAllowed(destination, customer)) return true;

        var assets = resolution.Assets.Where(x => x.Status == PanelFileAssetStatus.Active)
            .OrderBy(x => x.RelativePath ?? x.OriginalFileName, StringComparer.OrdinalIgnoreCase).ToList();
        if (assets.Count == 0) return false;

        foreach (var asset in assets)
        {
            if (Path.GetFileName(asset.OriginalFileName) != asset.OriginalFileName) return false;
            var pending = new Message
            {
                ConversationId = conversationId, Direction = MessageDirection.Outbound, MessageType = MessageType.Document,
                Content = $"Panel package: {resolution.CanonicalKey}; file: {asset.OriginalFileName}",
                DeliveryStatus = DeliveryStatus.Pending, SentBy = MessageSender.Bot,
            };
            db.Messages.Add(pending);
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                await using var source = await storage.OpenReadAsync(asset.StorageKey, cancellationToken);
                pending.ExternalMessageId = await client.SendDocumentMessageAsync(destination, source, asset.MimeType,
                    asset.OriginalFileName, "Panel paket dosyasi", cancellationToken);
                pending.DeliveryStatus = DeliveryStatus.Sent;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Package document delivery failed conversationId={ConversationId} package={PackageKey} file={FileName}",
                    conversationId, resolution.CanonicalKey, asset.OriginalFileName);
                pending.DeliveryStatus = DeliveryStatus.Failed;
                await db.SaveChangesAsync(CancellationToken.None);
                return true;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    private static bool TryParseExactCode(string? value, out PanelIdentity identity)
    {
        identity = new(null, null, null);
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Any(char.IsWhiteSpace)) return false;
        try
        {
            var parsed = PanelFolderPackageNameParser.Parse(value);
            identity = new(parsed.PixelPitch, parsed.PanelCode, parsed.SecondaryCode);
            return true;
        }
        catch (InvalidDataException) { return false; }
    }
}
