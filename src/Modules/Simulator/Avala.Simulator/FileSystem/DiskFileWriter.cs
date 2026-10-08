using Avala.Simulator.Playback;

namespace Avala.Simulator.FileSystem;

internal sealed class DiskFileWriter : IFileWriter
{
    public async ValueTask WriteAsync(string path, string content, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(path, content, cancellationToken);
}
