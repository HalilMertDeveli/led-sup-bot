namespace LedSupBot.Application.Abstractions;

/// <summary>
/// Private object storage for inbound label photos and outbound static assets.
/// Concrete provider (Azure Blob / S3 / Supabase) is deferred.
/// </summary>
public interface IMediaStorage
{
    Task<StoredMediaReference> StoreAsync(
        Stream content,
        string contentType,
        MediaStoreRequest request,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    bool Exists(string storageKey);
}

public sealed record MediaStoreRequest(
    string KeyPrefix,
    string FileExtension);

public sealed record StoredMediaReference(
    string StorageKey,
    string ContentType,
    long SizeBytes);
