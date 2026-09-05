using LedSupBot.Application.Abstractions;
using LedSupBot.Application.Media;
using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Media;

public sealed class LocalMediaStorage : IMediaStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalMediaStorage> _logger;

    public LocalMediaStorage(
        IOptions<MediaStorageOptions> options,
        ILogger<LocalMediaStorage> logger)
    {
        _logger = logger;
        var configured = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured));

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredMediaReference> StoreAsync(
        Stream content,
        string contentType,
        MediaStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        var storageKey = BuildStorageKey(request);
        var fullPath = ResolveSafePath(storageKey);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";
        long sizeBytes;
        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(file, cancellationToken);
                sizeBytes = file.Length;
            }

            File.Move(tempPath, fullPath, overwrite: false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        _logger.LogInformation(
            "Stored media object with key prefix {KeyPrefix} size {SizeBytes}",
            request.KeyPrefix,
            sizeBytes);

        return new StoredMediaReference(storageKey, contentType, sizeBytes);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveSafePath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Media object not found.", storageKey);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public bool Exists(string storageKey)
    {
        try
        {
            var fullPath = ResolveSafePath(storageKey);
            return File.Exists(fullPath);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveSafePath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    internal string ResolveSafePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        var normalizedKey = storageKey.Replace('\\', '/').Trim('/');
        if (normalizedKey.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalizedKey) ||
            normalizedKey.StartsWith('/'))
        {
            throw new ArgumentException("Invalid storage key.", nameof(storageKey));
        }

        var combined = Path.GetFullPath(Path.Combine(_rootPath, normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSep = _rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, _rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Storage key escapes media root.", nameof(storageKey));
        }

        return combined;
    }

    private static string BuildStorageKey(MediaStoreRequest request)
    {
        var prefix = SanitizeKeySegment(request.KeyPrefix);
        var extension = NormalizeExtension(request.FileExtension);
        var objectId = Guid.NewGuid().ToString("N");
        return $"{prefix}/{objectId}{extension}";
    }

    internal static string SanitizeKeySegment(string segment)
    {
        var cleaned = segment.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(cleaned) ||
            cleaned.Contains("..", StringComparison.Ordinal) ||
            cleaned.Contains(':'))
        {
            throw new ArgumentException("Invalid storage key prefix.", nameof(segment));
        }

        return cleaned;
    }

    private static string NormalizeExtension(string extension)
    {
        var ext = extension.Trim();
        if (string.IsNullOrWhiteSpace(ext))
        {
            return ".bin";
        }

        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        if (ext.Any(ch => ch is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
        {
            throw new ArgumentException("Invalid file extension.", nameof(extension));
        }

        return ext.ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup for temp files.
        }
    }
}
