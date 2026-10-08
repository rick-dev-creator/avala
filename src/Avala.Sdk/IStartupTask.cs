namespace Avala.Sdk;

public interface IStartupTask
{
    Task RunAsync(CancellationToken cancellationToken);
}
