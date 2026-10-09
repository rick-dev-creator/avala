using Avala.Agents.Contracts;
using Avala.Budgets.Admission;
using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Budgets;

public sealed class BudgetsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.budgets", "Budgets");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<IMachineBudgetFile, MachineBudgetFile>()
            .AddSingleton<RunningJobs>()
            .AddSingleton<IJobAdmission>(services => services.GetRequiredService<RunningJobs>())
            .AddSingleton<IHandle<JobProgressed>>(services => services.GetRequiredService<RunningJobs>())
            .AddSingleton<BudgetBook>()
            .AddSingleton<IBudgets>(services => services.GetRequiredService<BudgetBook>())
            .AddSingleton<IBudgetFiles, BudgetFileReader>()
            .AddSingleton<BudgetHolds>()
            .AddSingleton<IHandle<SessionOpened>, BudgetLoader>()
            .AddSingleton<BudgetEnforcer>()
            .AddSingleton<IHandle<BudgetLoaded>>(services => services.GetRequiredService<BudgetEnforcer>())
            .AddSingleton<IHandle<JobSessionStarted>>(services => services.GetRequiredService<BudgetEnforcer>())
            .AddSingleton<IHandle<JobProgressed>>(services => services.GetRequiredService<BudgetEnforcer>())
            .AddSingleton<IHandle<UsageRecorded>>(services => services.GetRequiredService<BudgetEnforcer>())
            .AddSingleton<IHandle<ResourcesSampled>>(services => services.GetRequiredService<BudgetEnforcer>());
    }
}
