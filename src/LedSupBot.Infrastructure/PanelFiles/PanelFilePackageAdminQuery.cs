using LedSupBot.Application.Abstractions;
using LedSupBot.Application.PanelFiles;
using LedSupBot.Domain.Entities;
using LedSupBot.Domain.PanelFiles;
using LedSupBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LedSupBot.Infrastructure.PanelFiles;

public sealed class PanelFilePackageAdminQuery(ApplicationDbContext db, IMediaStorage storage) : IPanelFilePackageAdminQuery
{
    public async Task<PanelFilePackageListResult> ListAsync(string? search, string? pitch, string? code,
        PanelFilePackageStatus? status, string? readiness, int limit, CancellationToken cancellationToken = default)
    {
        var query = db.PanelFilePackages.AsNoTracking().Include(x => x.Assets).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.CanonicalKey.Contains(term) || x.DisplayName.ToUpper().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(pitch)) query = query.Where(x => x.PixelPitch == pitch.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(code)) query = query.Where(x => x.PanelCode == code.Trim().ToUpper());
        if (status is not null) query = query.Where(x => x.Status == status);
        var boundedLimit = Math.Clamp(limit, 1, 200);
        if (string.IsNullOrWhiteSpace(readiness))
        {
            var total = await query.CountAsync(cancellationToken);
            var packages = await query.OrderByDescending(x => x.UpdatedAt).Take(boundedLimit).ToListAsync(cancellationToken);
            return new(packages.Select(Map).ToList(), total);
        }

        // Readiness includes private-storage existence and therefore cannot be translated to SQL.
        // Bound the candidate set before evaluating it, then paginate the filtered result.
        var candidates = await query.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        var filtered = candidates.Select(Map).Where(x => x.Readiness.Equals(readiness, StringComparison.OrdinalIgnoreCase)).ToList();
        return new(filtered.Take(boundedLimit).ToList(), filtered.Count);
    }

    public async Task<PanelFilePackageAdminDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await db.PanelFilePackages.AsNoTracking().Include(x => x.Assets).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)) is { } package
            ? Map(package) : null;

    private PanelFilePackageAdminDto Map(PanelFilePackage package)
    {
        var assets = package.Assets.OrderByDescending(x => x.Version).Select(x => new PanelFileAssetAdminDto(
            x.Id, x.AssetType, x.OriginalFileName, x.MimeType, x.FileSize, x.Sha256, x.Version, x.Status, x.CreatedAt)).ToList();
        var active = package.Assets.Where(x => x.Status == PanelFileAssetStatus.Active && storage.Exists(x.StorageKey)).ToList();
        var ready = package.Status == PanelFilePackageStatus.Active &&
                    active.Count(x => x.AssetType == PanelFileAssetType.FirmwareHex) == 1 &&
                    active.Count(x => x.AssetType == PanelFileAssetType.ReceiverConfigRcvbp) == 1;
        var readiness = package.Status != PanelFilePackageStatus.Active
            ? "NOT_DELIVERY_ELIGIBLE"
            : ready ? "READY" : "INCOMPLETE";
        return new(package.Id, package.PixelPitch, package.PanelCode, package.SecondaryCode, package.CanonicalKey,
            package.DisplayName, package.Manufacturer, package.Description, package.Notes, package.Status,
            package.Version, readiness, assets, package.UpdatedAt);
    }
}
