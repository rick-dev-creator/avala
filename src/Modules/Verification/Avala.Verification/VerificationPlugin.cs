using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;
using Avala.Verification.Evidence;
using Avala.Verification.Checks;
using Avala.Verification.Storage;
using Avala.Verification.Verifying;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Verification;

public sealed class VerificationPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.verification", "Verification");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteEvidenceStore>()
            .AddForwarded<IEvidenceStore, SqliteEvidenceStore>()
            .AddForwarded<IDiscoveryStore, SqliteEvidenceStore>()
            .AddForwarded<IStartupTask, SqliteEvidenceStore>()
            .AddSingleton<EvidenceBook>()
            .AddForwarded<IVerifications, EvidenceBook>()
            .AddForwarded<IStartupTask, EvidenceBook>()
            .AddSingleton<EvidenceLedger>()
            .AddSingleton<CheckRunner>()
            .AddSingleton<DiscoveryBook>()
            .AddForwarded<IChecksDiscoveries, DiscoveryBook>()
            .AddForwarded<IStartupTask, DiscoveryBook>()
            .AddSingleton<IDiscoverySettings, DiscoverySettings>()
            .AddSingleton<JobWorktrees>()
            .AddSingleton<DiscoveredJobs>()
            .AddSingleton<ScoutLauncher>()
            .AddSingleton<DiscoveredChecks>()
            .AddSingleton<AttemptChecks>()
            .AddSingleton<ProposalReview>()
            .AddSingleton<DiscoveryDesk>()
            .AddSingleton<ChecksScout>()
            .AddForwarded<IHandle<JobProgressed>, ChecksScout>()
            .AddForwarded<IHandle<StartupCompleted>, ChecksScout>()
            .AddSingleton<ICompletionGate, ChecksGate>()
            .AddSingleton<IRepositoryChecks, DeclaredChecks>()
            .AddSingleton<IRuleFileFormat, CheckFileFormat>();
    }
}
