using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Readying;

namespace Avala.Workbench.Tests.Readiness;

internal sealed class FakeForgeReadiness : IForgeReadiness
{
    public ForgeReadiness Shown { get; set; } = new(ForgeReadinessKind.NoRemote, string.Empty);

    public ValueTask<ForgeReadiness> OfAsync(string repository, CancellationToken cancellationToken) => ValueTask.FromResult(Shown);
}

internal sealed class FakeReadinessLinks : IReadinessLinks
{
    public List<string> Declared { get; } = [];

    public int SettingsOpened { get; private set; }

    public Task DeclareChecksAsync(string repository)
    {
        Declared.Add(repository);

        return Task.CompletedTask;
    }

    public void OpenSettings() => SettingsOpened++;
}

internal sealed class FakeDeliveryPlans : IDeliveryPlans
{
    public Result<DeliveryPlan, JobRejection> Plan { get; set; } = new DeliveryPlan("keep", "avala/fix-the-test", "main", "/worktrees/fix-the-test");

    public ValueTask<Result<DeliveryPlan, JobRejection>> PlanAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(Plan);
}
