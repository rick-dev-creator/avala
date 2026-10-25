using System.Collections.Immutable;
using System.Threading.Channels;

namespace Avala.Fleet.Recording;

internal sealed class ChangeSignal
{
    private ImmutableList<ChannelWriter<bool>> watchers = [];

    public ChannelReader<bool> Watch(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        ImmutableInterlocked.Update(ref watchers, list => list.Add(channel.Writer));
        cancellationToken.Register(() =>
        {
            ImmutableInterlocked.Update(ref watchers, list => list.Remove(channel.Writer));
            channel.Writer.TryComplete();
        });
        channel.Writer.TryWrite(true);

        return channel.Reader;
    }

    public void Raise()
    {
        foreach (var watcher in watchers)
        {
            watcher.TryWrite(true);
        }
    }
}
