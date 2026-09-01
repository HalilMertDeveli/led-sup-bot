using System.Threading.Channels;
using LedSupBot.Application.Abstractions;

namespace LedSupBot.Infrastructure.Queue;

/// <summary>
/// In-process foundation queue for local development wiring only.
/// Not a production WhatsApp processor and does not handle real channel events.
/// </summary>
public sealed class InMemoryBackgroundWorkQueue : IBackgroundWorkQueue
{
    private readonly Channel<BackgroundWorkItem> _channel =
        Channel.CreateUnbounded<BackgroundWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask QueueAsync(BackgroundWorkItem item, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(item, cancellationToken);

    public async IAsyncEnumerable<BackgroundWorkItem> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
