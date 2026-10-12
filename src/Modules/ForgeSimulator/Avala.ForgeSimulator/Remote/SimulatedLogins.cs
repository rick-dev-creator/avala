using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.ForgeSimulator.Remote;

internal sealed class SimulatedLogins : IForgeLogins
{
    public string Forge => SimulatedForge.Id;

    public Option<CliCall> Logins { get; } = new CliCall("git", ["--version"], CliStatusChannel.Output);

    public Option<string> SignInCommand => Option<string>.None;

    public IReadOnlyList<string> TokenVariables => [];

    public Option<ForgeProbe> Probe => Option<ForgeProbe>.None;

    public IReadOnlyList<DetectedLogin> Read(ProcessOutcome outcome) =>
        outcome.Succeeded ? [new DetectedLogin(SimulatedForge.Site, SimulatedRepositories.Owner)] : [];
}
