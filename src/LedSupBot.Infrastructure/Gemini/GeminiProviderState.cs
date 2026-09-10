using LedSupBot.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LedSupBot.Infrastructure.Gemini;

public enum GeminiCapacityStatus { Available, RateLimited, DailyQuotaExhausted, Unavailable, Misconfigured }

public enum GeminiFailureKind
{
    None, DailyQuota, TemporaryRateLimit, Unauthorized, InvalidRequest, ModelNotFound,
    TransientProviderFailure, Timeout, Network, InvalidResponse,
}

public sealed record GeminiProviderStatusSnapshot(
    GeminiCapacityStatus Status,
    GeminiFailureKind LastFailure,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SuppressedUntil,
    long RequestsAttempted,
    string CapacityMode,
    string Model);

public interface IGeminiProviderState { GeminiProviderStatusSnapshot Current { get; } }

public sealed class GeminiProviderState : IGeminiProviderState
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _concurrency;
    private readonly GeminiOptions _options;
    private GeminiProviderStatusSnapshot _current;

    public GeminiProviderState(IOptions<GeminiOptions> options)
    {
        _options = options.Value;
        _concurrency = new SemaphoreSlim(Math.Clamp(_options.MaxConcurrentRequests, 1, 32));
        var configured = !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.Model);
        _current = new(configured ? GeminiCapacityStatus.Available : GeminiCapacityStatus.Misconfigured,
            configured ? GeminiFailureKind.None : GeminiFailureKind.Unauthorized,
            DateTimeOffset.UtcNow, null, 0, _options.CapacityMode, _options.Model);
    }

    public GeminiProviderStatusSnapshot Current
    {
        get { lock (_gate) return RefreshSuppressionUnsafe(DateTimeOffset.UtcNow); }
    }

    internal async ValueTask<IDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var current = RefreshSuppressionUnsafe(DateTimeOffset.UtcNow);
            if (current.SuppressedUntil is { } until && until > DateTimeOffset.UtcNow) return null;
        }

        await _concurrency.WaitAsync(cancellationToken);
        lock (_gate)
        {
            var current = RefreshSuppressionUnsafe(DateTimeOffset.UtcNow);
            if (current.SuppressedUntil is { } until && until > DateTimeOffset.UtcNow)
            {
                _concurrency.Release();
                return null;
            }
        }
        return new Releaser(_concurrency);
    }

    internal void RecordAttempt()
    {
        lock (_gate) _current = _current with
        {
            RequestsAttempted = _current.RequestsAttempted + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    internal void MarkAvailable()
    {
        lock (_gate) _current = _current with
        {
            Status = GeminiCapacityStatus.Available,
            LastFailure = GeminiFailureKind.None,
            UpdatedAt = DateTimeOffset.UtcNow,
            SuppressedUntil = null,
        };
    }

    internal void MarkFailure(GeminiFailureKind failure, DateTimeOffset? suppressedUntil = null)
    {
        var status = failure switch
        {
            GeminiFailureKind.DailyQuota => GeminiCapacityStatus.DailyQuotaExhausted,
            GeminiFailureKind.TemporaryRateLimit => GeminiCapacityStatus.RateLimited,
            GeminiFailureKind.Unauthorized or GeminiFailureKind.InvalidRequest or GeminiFailureKind.ModelNotFound => GeminiCapacityStatus.Misconfigured,
            _ => GeminiCapacityStatus.Unavailable,
        };
        lock (_gate) _current = _current with
        {
            Status = status,
            LastFailure = failure,
            UpdatedAt = DateTimeOffset.UtcNow,
            SuppressedUntil = suppressedUntil,
        };
    }

    internal DateTimeOffset DailyResetBoundary()
    {
        var now = DateTimeOffset.UtcNow;
        return _options.DailyQuotaAware ? new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero) : now;
    }

    private GeminiProviderStatusSnapshot RefreshSuppressionUnsafe(DateTimeOffset now)
    {
        if (_current.SuppressedUntil is { } until && until <= now &&
            _current.Status is GeminiCapacityStatus.RateLimited or GeminiCapacityStatus.DailyQuotaExhausted)
        {
            _current = _current with
            {
                Status = GeminiCapacityStatus.Available,
                LastFailure = GeminiFailureKind.None,
                UpdatedAt = now,
                SuppressedUntil = null,
            };
        }
        return _current;
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
