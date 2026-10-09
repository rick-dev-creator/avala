using Avala.Autopilot.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class SourceFault : IPlugin, IJobSource
{
    private Exception? armed;

    public PluginInfo Info { get; } = new("source-fault", "Source fault");

    public string Name => "fault";

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddSingleton<IJobSource>(this);

    public void Arm(Exception fault) => Volatile.Write(ref armed, fault);

    public ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken) =>
        Volatile.Read(ref armed) is { } fault ? throw fault : ValueTask.FromResult<Result<SourceAnswer, AutopilotError>>(SourceAnswer.Nothing);

    public ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
