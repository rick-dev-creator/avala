using Avala.Contests.Contracts;
using Avala.Contests.Recovery;
using Avala.Contests.RepositoryFiles;
using Avala.Contests.Running;
using Avala.Contests.Storage;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Contests;

public sealed class ContestsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.contests", "Contests");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteContestStore>()
            .AddForwarded<IContestStore, SqliteContestStore>()
            .AddForwarded<IStartupTask, SqliteContestStore>()
            .AddSingleton<ContestBook>()
            .AddForwarded<IStartupTask, ContestBook>()
            .AddSingleton<IBestOfFile, BestOfReader>()
            .AddSingleton<CandidateConnections>()
            .AddSingleton<ContestStarter>()
            .AddSingleton<FactGatherer>()
            .AddSingleton<ContestJudge>()
            .AddSingleton<ContestDesk>()
            .AddForwarded<IHandle<JobProgressed>, ContestDesk>()
            .AddSingleton<ContestsEntry>()
            .AddForwarded<IContests, ContestsEntry>()
            .AddForwarded<IAnswerSharing, ContestsEntry>()
            .AddSingleton<IStartupTask, ContestRecovery>();
    }
}
