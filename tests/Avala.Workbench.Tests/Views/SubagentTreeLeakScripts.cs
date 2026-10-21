using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avala.Workbench.Tests.Overview;

namespace Avala.Workbench.Tests.Views;

[Collection(Leaks.Collection)]
public sealed class SubagentTreeLeakScripts
{
    [Fact]
    public async Task AnOrchestrationThatLeavesTheJobsIsLetGoWithItsSubagentsSoNothingOfItStaysInMemoryAsync()
    {
        using var bench = new TreeBench();
        bench.Forgets(bench.Root);
        using var page = bench.Page();
        var orchestration = bench.Orchestration("Ship the refunds flow");

        foreach (var agent in Enumerable.Range(0, 20).Select(index => $"agent-{index}"))
        {
            bench.Starts(orchestration, agent, $"Sub-agent {agent}");
            bench.Ends(orchestration, agent, ItemOutcome.Succeeded);
        }

        await bench.Ui.PresentedAsync(page, () => page.Trees.Count == 1, () => $"{page.Trees.Count} trees");
        var shown = await HeldAsync(bench, page);
        bench.Forgets(orchestration);
        await bench.Ui.PresentedAsync(page, () => page.Trees.Count == 0, () => $"{page.Trees.Count} trees");
        _ = Leaks.ManagedBytes();

        Assert.False(shown.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> HeldAsync(TreeBench bench, DelegationViewModel page) => new(await bench.Ui.ReadAsync(() => page.Trees[0]));
}
