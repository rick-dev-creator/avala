using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class LaunchFault : IPlugin
{
    private const string JobFile = ".avala/jobs.json";

    private Exception? armed;

    public PluginInfo Info { get; } = new("launch-fault", "Launch fault");

    public void Register(IPluginRegistrar registrar)
    {
        var files = registrar.Services.Single(descriptor => descriptor.ServiceType == typeof(IBaseFiles));
        registrar.Services.Remove(files);
        registrar.Services.AddSingleton<IBaseFiles>(services => new FaultyFiles(Real(services, files), this));
    }

    private static IBaseFiles Real(IServiceProvider services, ServiceDescriptor real) =>
        real.ImplementationInstance as IBaseFiles
        ?? real.ImplementationFactory?.Invoke(services) as IBaseFiles
        ?? (IBaseFiles)ActivatorUtilities.CreateInstance(services, real.ImplementationType!);

    public void Arm(Exception fault) => Volatile.Write(ref armed, fault);

    public void Disarm() => Volatile.Write(ref armed, null);

    private void ThrowIfArmed(string path)
    {
        if (path == JobFile && Volatile.Read(ref armed) is { } fault)
        {
            throw fault;
        }
    }

    private sealed class FaultyFiles(IBaseFiles inner, LaunchFault fault) : IBaseFiles
    {
        public ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken)
        {
            fault.ThrowIfArmed(path);

            return inner.ReadAsync(worktree, path, cancellationToken);
        }

        public ValueTask<Result<BaseFile, WorkspaceFailure>> ReadCurrentAsync(string repository, string path, CancellationToken cancellationToken) =>
            inner.ReadCurrentAsync(repository, path, cancellationToken);
    }
}
