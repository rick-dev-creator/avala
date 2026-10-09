using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class SimulatedProvider(Stagecraft craft) : IAgentProvider
{
    public ProviderInfo Info { get; } = new("simulator", "Simulated Claude Code");

    public AgentCapabilities Capabilities { get; } = new(
        StreamsPartialOutput: true,
        ExposesReasoning: true,
        CanInterrupt: true,
        CanResume: true,
        AcceptsTools: true,
        ReportsUsage: true,
        ReportsCost: true,
        ReportsLimits: true,
        AsksQuestions: true);

    public async ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken) =>
        await options.Resume.Match(
            token => Conversation.Mark(token).Match(
                async mark => (await craft.Library.NamedAsync(mark.Scenario, options, cancellationToken)).Match(
                    scenario => Start(options, mark.Resume(scenario)),
                    () => AgentError.CannotResume),
                () => Task.FromResult(Result<IAgentSession, AgentError>.Failure(AgentError.CannotResume))),
            () => Task.FromResult(Start(options, Option<Conversation>.None)));

    private Result<IAgentSession, AgentError> Start(SessionOptions options, Option<Conversation> resumed) =>
        Result<IAgentSession, AgentError>.Success(new SimulatedSession(options, craft, resumed));
}
