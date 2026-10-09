using System.Collections.Immutable;
using Avala.Sdk;

namespace Avala.Host.Tests;

internal sealed class RecordingLinks : ILinkOpener
{
    private ImmutableList<Uri> opened = [];

    public IReadOnlyList<Uri> Opened => Volatile.Read(ref opened);

    public ValueTask<Result<Uri, FileOpenError>> OpenAsync(Uri link, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref opened, list => list.Add(link));

        return ValueTask.FromResult<Result<Uri, FileOpenError>>(link);
    }
}
