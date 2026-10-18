using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeClipboard : IClipboard
{
    public List<string> Copied { get; } = [];

    public List<string> Images { get; } = [];

    public List<IReadOnlyList<string>> Files { get; } = [];

    public bool Refuses { get; init; }

    public ValueTask<bool> CopyAsync(string text, CancellationToken cancellationToken) => KeepAsync(Copied, text);

    public ValueTask<bool> CopyImageAsync(string path, CancellationToken cancellationToken) => KeepAsync(Images, path);

    public ValueTask<bool> CopyFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken) => KeepAsync(Files, paths);

    public ValueTask<bool> CopyLinkAsync(Uri link, CancellationToken cancellationToken) => KeepAsync(Copied, link.AbsoluteUri);

    private ValueTask<bool> KeepAsync<T>(List<T> into, T copied)
    {
        if (!Refuses)
        {
            into.Add(copied);
        }

        return ValueTask.FromResult(!Refuses);
    }
}
