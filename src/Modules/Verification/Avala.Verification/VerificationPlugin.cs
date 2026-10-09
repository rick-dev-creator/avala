using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Verification.Evidence;
using Avala.Verification.FileSystem;
using Avala.Verification.Verifying;
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
            .AddSingleton<EvidenceBook>()
            .AddSingleton<IVerifications>(services => services.GetRequiredService<EvidenceBook>())
            .AddSingleton<EvidenceLedger>()
            .AddSingleton<ICheckDeclarations, RepositoryDeclarations>()
            .AddSingleton<CheckRunner>()
            .AddSingleton<ICompletionGate, ChecksGate>();
    }
}
