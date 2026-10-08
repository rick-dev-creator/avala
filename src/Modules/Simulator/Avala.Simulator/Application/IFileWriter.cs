namespace Avala.Simulator.Application;

internal interface IFileWriter
{
    ValueTask WriteAsync(string path, string content, CancellationToken cancellationToken);
}
