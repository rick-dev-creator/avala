using Avala.Budgets.Admission;
using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Budgets.Tests.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Budgets.Tests;

public sealed class BudgetsPluginTests
{
    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task EachServiceOfferedUnderSeveralContractsIsOneInstanceAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Same(composition.Get<RunningJobs>(), composition.Get<IJobAdmission>());
        Assert.Contains(composition.Get<RunningJobs>(), composition.All<IHandle<JobProgressed>>());
        Assert.Same(composition.Get<BudgetBook>(), composition.Get<IBudgets>());
        Assert.Contains(composition.Get<BudgetBook>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<BudgetFileReader>(), composition.Get<IBudgetFiles>());
        Assert.Same(composition.Get<BudgetFileReader>(), composition.Get<IRepositoryBudgets>());
        Assert.Same(composition.Get<BudgetEnforcer>(), composition.Get<IHandle<UsageRecorded>>());
        Assert.Contains(composition.Get<BudgetEnforcer>(), composition.All<IHandle<JobProgressed>>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new BudgetsPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IJobs>(new Budgeted.HoldingJobs(default))
            .AddSingleton<IUsage>(new Budgeted.Usage())
            .AddSingleton<IResources>(new Budgeted.MeasuredResources())
            .AddSingleton<IBaseFiles>(new CommittedFiles()));
}
