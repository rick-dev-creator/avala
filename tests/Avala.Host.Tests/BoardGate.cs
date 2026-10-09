using Avala.Jobs.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class BoardGate : IPlugin
{
    private const string Keeper = "Avala.Workbench.Board.BoardKeeper";

    private Hold? armed;
    private Hold? holding;

    public PluginInfo Info { get; } = new("board-gate", "Board gate");

    public void Register(IPluginRegistrar registrar)
    {
        var keeper = registrar.Services.Single(descriptor => descriptor.ServiceType.FullName == Keeper);
        registrar.Services.Remove(keeper);
        registrar.Services.AddSingleton(
            keeper.ServiceType,
            services => ActivatorUtilities.CreateInstance(services, keeper.ServiceType, new GatedCatalog(services.GetRequiredService<IJobCatalog>(), this)));
    }

    public Task HoldNextAsync()
    {
        var hold = new Hold();
        Volatile.Write(ref armed, hold);

        return hold.Held.Task;
    }

    public void Release() => Volatile.Read(ref holding)?.Released.TrySetResult();

    private async Task PassAsync()
    {
        if (Interlocked.Exchange(ref armed, null) is { } hold)
        {
            Volatile.Write(ref holding, hold);
            hold.Held.TrySetResult();
            await hold.Released.Task;
        }
    }

    private sealed class Hold
    {
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedCatalog(IJobCatalog inner, BoardGate gate) : IJobCatalog
    {
        public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);

        public async ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken)
        {
            await gate.PassAsync();

            return await inner.HistoryAsync(job, cancellationToken);
        }

        public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => inner.ChildrenAsync(parent, cancellationToken);

        public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => inner.TreeAsync(root, cancellationToken);
    }
}
