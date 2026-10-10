using Avala.Agents.Contracts;
using Avala.Budgets.Admission;
using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Budgets.Routing;
using Avala.Budgets.Storage;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;
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
            .AddForwarded<IJobAdmission, RunningJobs>()
            .AddForwarded<IHandle<JobProgressed>, RunningJobs>()
            .AddSingleton<SqliteInterventionStore>()
            .AddForwarded<IInterventionStore, SqliteInterventionStore>()
            .AddForwarded<IStartupTask, SqliteInterventionStore>()
            .AddSingleton<BudgetBook>()
            .AddForwarded<IBudgets, BudgetBook>()
            .AddForwarded<IStartupTask, BudgetBook>()
            .AddSingleton<BudgetFileReader>()
            .AddSingleton<IRuleFileFormat, BudgetFileFormat>()
            .AddForwarded<IBudgetFiles, BudgetFileReader>()
            .AddForwarded<IRepositoryBudgets, BudgetFileReader>()
            .AddSingleton<IConnectionSelector, CapacitySelector>()
            .AddSingleton<BudgetActions>()
            .AddSingleton<IHandle<SessionOpened>, BudgetLoader>()
            .AddSingleton<JobBreaches>()
            .AddSingleton<BudgetEnforcer>()
            .AddForwarded<IHandle<BudgetLoaded>, BudgetEnforcer>()
            .AddForwarded<IHandle<JobSessionStarted>, BudgetEnforcer>()
            .AddForwarded<IHandle<JobProgressed>, BudgetEnforcer>()
            .AddForwarded<IHandle<UsageRecorded>, BudgetEnforcer>()
            .AddForwarded<IHandle<ResourcesSampled>, BudgetEnforcer>()
            .AddForwarded<IHandle<JobSubmitted>, BudgetEnforcer>()
            .AddForwarded<IHandle<JobHeld>, BudgetEnforcer>()
            .AddSingleton<ICompletionGate, SpendGate>();
    }
}
