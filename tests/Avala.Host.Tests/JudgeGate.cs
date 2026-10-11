using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class JudgeGate : IPlugin
{
    private const string Gatherer = "Avala.Contests.Running.FactGatherer";

    private TaskCompletionSource? armed;

    public PluginInfo Info { get; } = new("judge-gate", "Judge gate");

    public void Register(IPluginRegistrar registrar)
    {
        var gatherer = registrar.Services.Single(descriptor => descriptor.ServiceType.FullName == Gatherer);
        registrar.Services.Remove(gatherer);
        registrar.Services.AddSingleton(
            gatherer.ServiceType,
            services => ActivatorUtilities.CreateInstance(services, gatherer.ServiceType, new GatedEvidence(services.GetRequiredService<IRunEvidence>(), this)));
    }

    public Task HoldNextAsync()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref armed, held);

        return held.Task;
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref armed, null) is { } held)
        {
            held.TrySetResult();
            await cancellationToken.UntilCancelledAsync();
        }
    }

    private sealed class GatedEvidence(IRunEvidence inner, JudgeGate gate) : IRunEvidence
    {
        public async ValueTask<Option<RunEvidence>> OfJobAsync(JobId job, CancellationToken cancellationToken)
        {
            await gate.PassAsync(cancellationToken);

            return await inner.OfJobAsync(job, cancellationToken);
        }
    }
}
