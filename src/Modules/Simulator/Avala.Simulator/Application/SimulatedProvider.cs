using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Application;

internal sealed class SimulatedProvider(IFileWriter files, Pacing pacing) : IAgentProvider
{
    public ProviderInfo Info { get; } = new("simulator", "Simulated Claude Code");

    public AgentCapabilities Capabilities { get; } = new(
        StreamsPartialOutput: true,
        ExposesReasoning: true,
        CanInterrupt: true,
        CanResume: true,
        ReportsUsage: true,
        ReportsCost: true,
        ReportsLimits: true);

    public ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<IAgentSession, AgentError>.Success(new SimulatedSession(options, files, pacing)));
}
