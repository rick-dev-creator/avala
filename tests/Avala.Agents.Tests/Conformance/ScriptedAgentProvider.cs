using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Tests.Conformance;

internal sealed class ScriptedAgentProvider(Func<SessionId, TurnId, IEnumerable<IAgentEvent>> script) : IAgentProvider
{
    public ProviderInfo Info { get; } = new("scripted", "Scripted");

    public AgentCapabilities Capabilities { get; } = new(
        StreamsPartialOutput: true,
        ExposesReasoning: true,
        CanInterrupt: false,
        CanResume: false,
        ReportsUsage: true,
        ReportsCost: true,
        ReportsLimits: true);

    public ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<IAgentSession, AgentError>.Success(new ScriptedSession(script)));
}
