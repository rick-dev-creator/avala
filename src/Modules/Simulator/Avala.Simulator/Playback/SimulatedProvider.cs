using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class SimulatedProvider(IFileWriter files, Pacing pacing) : IAgentProvider
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

    public ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken) =>
        ValueTask.FromResult(options.Resume.Match(
            token => Conversation.Resume(token).Match(
                conversation => Start(options, conversation),
                () => Result<IAgentSession, AgentError>.Failure(AgentError.CannotResume)),
            () => Start(options, Option<Conversation>.None)));

    private Result<IAgentSession, AgentError> Start(SessionOptions options, Option<Conversation> resumed) =>
        Result<IAgentSession, AgentError>.Success(new SimulatedSession(options, files, pacing, resumed));
}
