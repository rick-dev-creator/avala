using System.Collections.Concurrent;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Testing;

public sealed class ScriptedAgentProvider(Func<SessionId, TurnId, IEnumerable<IAgentEvent>> script, bool canInterrupt = false) : IAgentProvider
{
    private readonly ConcurrentQueue<ScriptedSession> sessions = new();

    public ProviderInfo Info { get; } = new("scripted", "Scripted");

    public AgentCapabilities Capabilities { get; } = new(
        StreamsPartialOutput: true,
        ExposesReasoning: true,
        CanInterrupt: canInterrupt,
        CanResume: false,
        ReportsUsage: true,
        ReportsCost: true,
        ReportsLimits: true);

    public IReadOnlyList<ScriptedSession> Sessions => [.. sessions];

    public static IEnumerable<IAgentEvent> Reply(SessionId session, TurnId turn) =>
    [
        new TurnStarted(session, turn),
        new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"),
        new ItemProgressed(session, turn, new ItemId("reply"), "Done."),
        new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded),
        new TurnCompleted(session, turn, TurnOutcome.Finished),
    ];

    public ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken)
    {
        var session = new ScriptedSession(script, options);
        sessions.Enqueue(session);

        return ValueTask.FromResult(Result<IAgentSession, AgentError>.Success(session));
    }
}
