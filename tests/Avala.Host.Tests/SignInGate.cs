using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Host.Tests;

internal sealed class SignInGate : IPlugin
{
    public PluginInfo Info { get; } = new("sign-in-gate", "Sign-in gate");

    public List<IForgeLogins> Logins { get; } = [];

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.RemoveAll<IForgeLogins>();

        foreach (var logins in Logins)
        {
            registrar.Services.AddSingleton(logins);
        }
    }
}

internal sealed class RecordedLogins(string forge, IReadOnlyList<DetectedLogin> found) : IForgeLogins
{
    public string Forge => forge;

    public Option<CliCall> Logins { get; } = new CliCall("git", ["--version"], CliStatusChannel.Output);

    public Option<string> SignInCommand => $"{forge} auth login";

    public IReadOnlyList<string> TokenVariables => [];

    public Option<ForgeProbe> Probe => Option<ForgeProbe>.None;

    public IReadOnlyList<DetectedLogin> Read(ProcessOutcome outcome) => outcome.Succeeded ? found : [];
}
