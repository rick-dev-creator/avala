namespace Avala.Simulator.Playback;

internal interface IFileWriter
{
    ValueTask WriteAsync(string path, string content, CancellationToken cancellationToken);
}
