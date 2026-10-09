namespace Avala.Sdk;

public interface IShutdownTask
{
    Task StopAsync(CancellationToken cancellationToken);
}
