namespace LedSupBot.Application.Abstractions;

/// <summary>
/// Abstraction for future async work (inbound messages, media, AI, outbound, retries).
/// Real queue backends are deferred; Phase 2 only defines the contract.
/// </summary>
public interface IBackgroundWorkQueue
{
    ValueTask QueueAsync(BackgroundWorkItem item, CancellationToken cancellationToken = default);

    IAsyncEnumerable<BackgroundWorkItem> DequeueAllAsync(CancellationToken cancellationToken);
}

public sealed record BackgroundWorkItem(
    Guid Id,
    string WorkType,
    string PayloadJson,
    DateTimeOffset EnqueuedAtUtc);
