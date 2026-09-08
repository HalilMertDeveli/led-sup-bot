using System.Security.Cryptography;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.PanelFiles;

public sealed class PanelFolderImportService(
    ApplicationDbContext db,
    IPanelIdentityNormalizer normalizer,
    IMediaStorage storage,
    IOptions<PanelFilePackageOptions> options) : IPanelFolderImportService
{
    public async Task<PanelFilePackage> ImportAsync(string folderName, IReadOnlyList<PanelFolderImportFile> files,
        CancellationToken cancellationToken = default)
    {
        var identity = ParseFolderName(folderName);
        if (files.Count < options.Value.MinImportedFileCount)
            throw new InvalidDataException("Pakette en az 2 dosya olmalı.");

        var safeFiles = files.Select(ValidateFile).ToList();
        PanelFolderImportMultipartFactory.ValidateMinimumAssets(safeFiles.Select(x => x.FileName).ToList());
        var duplicateOutgoingName = safeFiles.GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateOutgoingName is not null)
            throw new InvalidDataException($"Aynı gönderim adına sahip birden fazla dosya var: {duplicateOutgoingName.Key}");

        var stored = new List<(PanelFolderImportFile File, StoredMediaReference Stored, string Hash)>();
        try
        {
            foreach (var file in safeFiles)
            {
                await using var buffer = new MemoryStream();
                await CopyBoundedAsync(file.Content, buffer, options.Value.MaxImportedFileSizeBytes, cancellationToken);
                if (buffer.Length == 0) throw new InvalidDataException($"Boş dosya içe aktarılamaz: {file.FileName}");
                var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
                buffer.Position = 0;
                var saved = await storage.StoreAsync(buffer, NormalizeContentType(file.ContentType),
                    new MediaStoreRequest($"panel-packages/folder-import/{identity.CanonicalKey}", Path.GetExtension(file.FileName)), cancellationToken);
                stored.Add((file, saved, hash));
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var package = await db.PanelFilePackages.Include(x => x.Assets)
                .SingleOrDefaultAsync(x => x.CanonicalKey == identity.CanonicalKey, cancellationToken);
            if (package is null)
            {
                package = new PanelFilePackage
                {
                    PixelPitch = identity.PixelPitch!, PanelCode = identity.PanelCode!, SecondaryCode = identity.SecondaryCode!,
                    CanonicalKey = identity.CanonicalKey!, DisplayName = folderName.Trim(), Version = 1
                };
                db.PanelFilePackages.Add(package);
            }
            else
            {
                package.Version++;
                foreach (var old in package.Assets.Where(x => x.Status == PanelFileAssetStatus.Active))
                    old.Status = PanelFileAssetStatus.Archived;
                // Release filtered active-path uniqueness before inserting the new version.
                // Both operations remain invisible until this transaction commits.
                await db.SaveChangesAsync(cancellationToken);
            }

            package.OriginalFolderName = folderName.Trim();
            package.ImportedAt = DateTimeOffset.UtcNow;
            package.Status = PanelFilePackageStatus.Active;
            package.DisplayName = folderName.Trim();
            foreach (var item in stored)
            {
                var asset = new PanelFilePackageAsset
                {
                    PanelFilePackageId = package.Id,
                    AssetType = Classify(item.File.FileName), Status = PanelFileAssetStatus.Active,
                    Version = package.Version, OriginalFileName = item.File.FileName,
                    RelativePath = item.File.RelativePath, StorageKey = item.Stored.StorageKey,
                    MimeType = item.Stored.ContentType, FileSize = item.Stored.SizeBytes, Sha256 = item.Hash
                };
                db.PanelFilePackageAssets.Add(asset);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return package;
        }
        catch
        {
            foreach (var item in stored)
                try { await storage.DeleteAsync(item.Stored.StorageKey, CancellationToken.None); } catch { }
            throw;
        }
    }

    private PanelIdentity ParseFolderName(string folderName)
    {
        var parsed = PanelFolderPackageNameParser.Parse(folderName);
        var normalized = normalizer.Normalize(parsed.PixelPitch, parsed.PanelCode, parsed.SecondaryCode);
        return normalized.Status == PanelIdentityNormalizationStatus.Valid && normalized.Identity?.CanonicalKey is not null
            ? normalized.Identity
            : throw new ArgumentException("Bu klasör adı beklenen P+Chipset+Decoder formatında değil.");
    }

    private static PanelFolderImportFile ValidateFile(PanelFolderImportFile file)
    {
        var relative = (file.RelativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (relative.Length == 0 || relative.Length > 255 || relative.Contains('/') || Path.IsPathRooted(relative))
            throw new ArgumentException("Geçersiz göreli dosya yolu.");
        var fileName = Path.GetFileName(relative);
        if (!string.Equals(fileName, file.FileName, StringComparison.Ordinal) || fileName.Length > 255)
            throw new ArgumentException("Geçersiz dosya adı.");
        return file with { RelativePath = relative };
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long max, CancellationToken ct)
    {
        if (max <= 0) throw new InvalidOperationException("Dosya boyutu sınırı yapılandırılmamış.");
        var buffer = new byte[81920]; long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct); if (read == 0) break;
            total += read; if (total > max) throw new InvalidDataException("İçe aktarılan dosya boyut sınırını aşıyor.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static PanelFileAssetType Classify(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".hex" => PanelFileAssetType.FirmwareHex,
        ".rcvbp" => PanelFileAssetType.ReceiverConfigRcvbp,
        _ => PanelFileAssetType.PackageFile
    };
    private static string NormalizeContentType(string value) => string.IsNullOrWhiteSpace(value) ? "application/octet-stream" : value[..Math.Min(value.Length, 128)];
}
