using LedSupBot.Application;
using LedSupBot.Application.Abstractions;
using LedSupBot.Infrastructure;

namespace LedSupBot.Workers;

/// <summary>
/// Foundation hosted service only. Does not process real WhatsApp, media, or AI jobs.
/// Future phases will enqueue real work types and register real handlers.
/// </summary>
public sealed class FoundationHeartbeatWorker : BackgroundService
{
    private readonly ILogger<FoundationHeartbeatWorker> _logger;
    private readonly IBackgroundWorkQueue _queue;

    public FoundationHeartbeatWorker(
        ILogger<FoundationHeartbeatWorker> logger,
        IBackgroundWorkQueue queue)
    {
        _logger = logger;
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LedSupBot.Workers foundation heartbeat started. No WhatsApp/AI processing is active in Phase 2.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Intentionally idle: proves hosting + DI wiring without inventing fake channel processing.
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
