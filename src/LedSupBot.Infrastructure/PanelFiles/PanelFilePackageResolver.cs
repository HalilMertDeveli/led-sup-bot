using System.Security.Cryptography;
using LedSupBot.Application.Abstractions;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Infrastructure.PanelFiles;

public sealed class PanelFilePackageResolver(
    ApplicationDbContext db,
    IPanelIdentityNormalizer normalizer,
    IMediaStorage storage) : IPanelFilePackageResolver
{
    public async Task<PanelFilePackageResolution> ResolveAsync(PanelIdentity identity, CancellationToken cancellationToken = default)
    {
        var normalized = normalizer.Normalize(identity.PixelPitch, identity.PanelCode, identity.SecondaryCode);
        if (normalized.Status == PanelIdentityNormalizationStatus.MissingInformation)
            return new(PanelFilePackageResolutionStatus.MissingInformation);
        if (normalized.Status != PanelIdentityNormalizationStatus.Valid || normalized.Identity?.CanonicalKey is null)
            return new(PanelFilePackageResolutionStatus.NoMatch);

        var key = normalized.Identity.CanonicalKey;
        var packages = await db.PanelFilePackages.AsNoTracking().Include(x => x.Assets)
            .Where(x => x.CanonicalKey == key).Take(2).ToListAsync(cancellationToken);
        if (packages.Count == 0) return new(PanelFilePackageResolutionStatus.NoMatch, CanonicalKey: key);
        if (packages.Count > 1) return new(PanelFilePackageResolutionStatus.Ambiguous, CanonicalKey: key);

        var package = packages[0];
        if (package.Status != PanelFilePackageStatus.Active)
            return new(PanelFilePackageResolutionStatus.InactivePackage, package.Id, key);
        var active = package.Assets.Where(x => x.Status == PanelFileAssetStatus.Active && storage.Exists(x.StorageKey)).ToList();
        var ready = package.OriginalFolderName is not null
            ? active.Count > 0
            : active.Count(x => x.AssetType == PanelFileAssetType.FirmwareHex) == 1 &&
              active.Count(x => x.AssetType == PanelFileAssetType.ReceiverConfigRcvbp) == 1;
        return ready
            ? new(PanelFilePackageResolutionStatus.ExactMatch, package.Id, key, active, package.OriginalFolderName is not null)
            : new(PanelFilePackageResolutionStatus.IncompletePackage, package.Id, key, active, package.OriginalFolderName is not null);
    }
}

public sealed class PanelFileAssetRegistry(
    ApplicationDbContext db,
    IMediaStorage storage) : IPanelFileAssetRegistry
{
    public async Task<PanelFilePackageAsset> StoreAsync(Guid packageId, PanelFileAssetType expectedType, string originalFileName, Stream content,
        long maxSizeBytes = 25 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 255 ||
            originalFileName.Contains('/') || originalFileName.Contains('\\') ||
            Path.GetFileName(originalFileName) != originalFileName || IsReservedFileName(originalFileName) ||
            !PanelFileAssetPolicy.TryClassify(originalFileName, out var type))
            throw new ArgumentException("Only safe .hex and .rcvbp filenames are accepted.", nameof(originalFileName));
        if (type != expectedType) throw new ArgumentException("The file extension does not match the requested package slot.", nameof(originalFileName));
        if (maxSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxSizeBytes));
        if (!await db.PanelFilePackages.AnyAsync(x => x.Id == packageId, cancellationToken))
            throw new KeyNotFoundException("Panel file package was not found.");

        await using var buffer = new MemoryStream();
        var chunk = new byte[81920]; long total = 0;
        while (true)
        {
            var read = await content.ReadAsync(chunk, cancellationToken); if (read == 0) break;
            total += read; if (total > maxSizeBytes) throw new InvalidDataException("Panel package asset exceeds the size limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (total == 0) throw new InvalidDataException("Panel package asset is empty.");
        var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        var previous = await db.PanelFilePackageAssets
            .Where(x => x.PanelFilePackageId == packageId && x.AssetType == type && x.Status == PanelFileAssetStatus.Active)
            .OrderByDescending(x => x.Version).ToListAsync(cancellationToken);
        if (previous.Any(x => x.Sha256 == hash)) throw new InvalidOperationException("This exact asset version is already registered.");
        buffer.Position = 0;
        StoredMediaReference? stored = null;
        try
        {
            stored = await storage.StoreAsync(buffer, "application/octet-stream",
                new MediaStoreRequest($"panel-packages/{packageId:N}", Path.GetExtension(originalFileName)), cancellationToken);
            await using (var storedStream = await storage.OpenReadAsync(stored.StorageKey, cancellationToken))
            {
                var storedHash = Convert.ToHexString(await SHA256.HashDataAsync(storedStream, cancellationToken)).ToLowerInvariant();
                if (stored.SizeBytes != total || storedHash != hash) throw new IOException("Stored panel asset failed integrity verification.");
            }

            foreach (var item in previous) item.Status = PanelFileAssetStatus.Archived;
            var asset = new PanelFilePackageAsset
            {
                PanelFilePackageId = packageId, AssetType = type, OriginalFileName = originalFileName,
                StorageKey = stored.StorageKey, MimeType = stored.ContentType, FileSize = stored.SizeBytes,
                Sha256 = hash, Version = previous.Count == 0 ? 1 : previous.Max(x => x.Version) + 1
            };
            db.PanelFilePackageAssets.Add(asset); await db.SaveChangesAsync(cancellationToken); return asset;
        }
        catch
        {
            if (stored is not null)
            {
                try { await storage.DeleteAsync(stored.StorageKey, CancellationToken.None); }
                catch { /* Preserve the original failure; orphan cleanup is best effort. */ }
            }
            throw;
        }
    }

    private static bool IsReservedFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).TrimEnd(' ', '.').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
               (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }
}

public sealed class PanelFilePackageRegistry(
    ApplicationDbContext db,
    IPanelIdentityNormalizer normalizer,
    IMediaStorage storage) : IPanelFilePackageRegistry
{
    public async Task<PanelFilePackage> CreateDraftAsync(string pixelPitch, string panelCode, string secondaryCode,
        string? displayName = null, CancellationToken cancellationToken = default)
    {
        var normalized = normalizer.Normalize(pixelPitch, panelCode, secondaryCode);
        if (normalized.Status != PanelIdentityNormalizationStatus.Valid || normalized.Identity?.CanonicalKey is null)
            throw new ArgumentException("A complete valid panel identity is required.");
        var identity = normalized.Identity;
        if (await db.PanelFilePackages.AnyAsync(x => x.CanonicalKey == identity.CanonicalKey, cancellationToken))
            throw new InvalidOperationException("A package with this canonical identity already exists.");
        var package = new PanelFilePackage
        {
            PixelPitch = identity.PixelPitch!, PanelCode = identity.PanelCode!, SecondaryCode = identity.SecondaryCode!,
            CanonicalKey = identity.CanonicalKey, DisplayName = string.IsNullOrWhiteSpace(displayName) ? identity.CanonicalKey : displayName.Trim()
        };
        db.PanelFilePackages.Add(package); await db.SaveChangesAsync(cancellationToken); return package;
    }

    public async Task<bool> ActivateAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var package = await db.PanelFilePackages.Include(x => x.Assets).SingleOrDefaultAsync(x => x.Id == packageId, cancellationToken);
        if (package is null || package.Status == PanelFilePackageStatus.Archived) return false;
        var active = package.Assets.Where(x => x.Status == PanelFileAssetStatus.Active && storage.Exists(x.StorageKey)).ToList();
        var ready = package.OriginalFolderName is not null
            ? active.Count > 0
            : active.Count(x => x.AssetType == PanelFileAssetType.FirmwareHex) == 1 &&
              active.Count(x => x.AssetType == PanelFileAssetType.ReceiverConfigRcvbp) == 1;
        if (!ready) return false;
        package.Status = PanelFilePackageStatus.Active; await db.SaveChangesAsync(cancellationToken); return true;
    }

    public async Task<bool> ArchiveAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var package = await db.PanelFilePackages.SingleOrDefaultAsync(x => x.Id == packageId, cancellationToken);
        if (package is null) return false; package.Status = PanelFilePackageStatus.Archived;
        await db.SaveChangesAsync(cancellationToken); return true;
    }

    public async Task<PanelFilePackage?> UpdateDraftAsync(Guid packageId, string pixelPitch, string panelCode, string secondaryCode,
        string? displayName, string? manufacturer, string? description, string? notes, int version,
        CancellationToken cancellationToken = default)
    {
        var package = await db.PanelFilePackages.Include(x => x.Assets).SingleOrDefaultAsync(x => x.Id == packageId, cancellationToken);
        if (package is null || package.Status != PanelFilePackageStatus.Draft || package.Assets.Any() || package.Version != version) return null;
        var result = normalizer.Normalize(pixelPitch, panelCode, secondaryCode);
        if (result.Status != PanelIdentityNormalizationStatus.Valid || result.Identity?.CanonicalKey is null) return null;
        if (await db.PanelFilePackages.AnyAsync(x => x.Id != packageId && x.CanonicalKey == result.Identity.CanonicalKey, cancellationToken))
            throw new InvalidOperationException("A package with this canonical identity already exists.");
        package.PixelPitch = result.Identity.PixelPitch!; package.PanelCode = result.Identity.PanelCode!;
        package.SecondaryCode = result.Identity.SecondaryCode!; package.CanonicalKey = result.Identity.CanonicalKey;
        package.DisplayName = string.IsNullOrWhiteSpace(displayName) ? package.CanonicalKey : displayName.Trim();
        package.Manufacturer = Trim(manufacturer, 128); package.Description = Trim(description, 2000); package.Notes = Trim(notes, 2000);
        package.Version++; await db.SaveChangesAsync(cancellationToken); return package;
    }

    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
