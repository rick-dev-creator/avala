using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
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
            .AddForwarded<IStartupTask, SqliteEvidenceStore>()
            .AddSingleton<EvidenceBook>()
            .AddForwarded<IVerifications, EvidenceBook>()
            .AddForwarded<IStartupTask, EvidenceBook>()
            .AddSingleton<EvidenceLedger>()
            .AddSingleton<CheckRunner>()
            .AddSingleton<ICompletionGate, ChecksGate>()
            .AddSingleton<IRepositoryChecks, DeclaredChecks>()
            .AddSingleton<IRuleFileFormat, CheckFileFormat>();
    }
}
