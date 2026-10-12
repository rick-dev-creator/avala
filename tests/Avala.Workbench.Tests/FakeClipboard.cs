using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeClipboard : IClipboard
{
    public List<string> Copied { get; } = [];

    public bool Refuses { get; init; }

    public ValueTask<bool> CopyAsync(string text, CancellationToken cancellationToken)
    {
        if (!Refuses)
        {
            Copied.Add(text);
        }

        return ValueTask.FromResult(!Refuses);
    }
}
