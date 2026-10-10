using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class SimulatedProvider(Stagecraft craft, ProviderInfo info) : IAgentProvider
{
    public const string Id = "simulator";

    public const string SecondId = "simulator-second";

    public SimulatedProvider(Stagecraft craft)
        : this(craft, First(developer: true))
    {
    }

    public static ProviderInfo Second { get; } = new(SecondId, "Second simulated harness") { OffersImplicitConnection = false };

    public ProviderInfo Info { get; } = info;

    public static ProviderInfo First(bool developer) => new(Id, "Simulated Claude Code") { OffersImplicitConnection = developer };

    public CapabilitySet CapabilitiesOn(ConnectionEnvironment connection) => SimulatedCapabilities.On(Info.Id, connection);

    public async ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken)
    {
        var replayed = await SimulatedAccounts.Replay(options.Connection).Match(
            recording => craft.Library.NamedAsync($"{ReplayRequest.CompressedTag[1..]}{recording}", options, cancellationToken),
            () => Task.FromResult(Option<Scenario>.None));
        var account = replayed.Match(scenario => scenario.Account, () => SimulatedAccounts.For(options.Connection));

        return await options.Resume.Match(
            token => ResumeAsync(options, account, token, cancellationToken),
            () => Task.FromResult(Start(options, account, replayed.Map(scenario => Conversation.Begin(scenario, SimulatedAccounts.Holder(account))))));
    }

    private async Task<Result<IAgentSession, AgentError>> ResumeAsync(
        SessionOptions options,
        Option<AgentAccount> account,
        ResumeToken token,
        CancellationToken cancellationToken) =>
        await Conversation.Mark(token)
            .Bind(mark => mark.Holder == SimulatedAccounts.Holder(account) ? mark : Option<ConversationMark>.None)
            .Match(
                async mark => (await craft.Library.NamedAsync(mark.Scenario, options, cancellationToken)).Match(
                    scenario => Start(options, account, mark.Resume(scenario)),
                    () => AgentError.CannotResume),
                () => Task.FromResult(Result<IAgentSession, AgentError>.Failure(AgentError.CannotResume)));

    private Result<IAgentSession, AgentError> Start(SessionOptions options, Option<AgentAccount> account, Option<Conversation> conversation) =>
        Result<IAgentSession, AgentError>.Success(new SimulatedSession(options, craft, new Casting(account, CapabilitiesOn(options.Connection)), conversation));
}
