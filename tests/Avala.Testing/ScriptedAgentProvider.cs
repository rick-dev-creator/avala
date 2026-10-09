using System.Collections.Concurrent;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Testing;

public sealed class ScriptedAgentProvider(Func<SessionId, TurnId, IEnumerable<IAgentEvent>> script, bool canInterrupt = false) : IAgentProvider
{
    private readonly ConcurrentQueue<ScriptedSession> sessions = new();

    public ProviderInfo Info { get; init; } = new("scripted", "Scripted");

    public CapabilitySet Capabilities { get; init; } = Declared.With(canInterrupt ? [new Interruptible()] : []);

    public Func<ConnectionEnvironment, CapabilitySet, CapabilitySet> OnConnection { get; init; } = (_, declared) => declared;

    public static CapabilitySet Declared { get; } = CapabilitySet.Of(
        new StreamsPartialOutput(),
        new ExposesReasoning(),
        new ReportsUsage(),
        new ReportsCost("USD"),
        new ReportsLimits(["5h", "7d"]));

    public Func<SessionId, TurnId, string, IEnumerable<IAgentEvent>> MidTurn { get; init; } =
        (session, turn, text) => [new MessageQueued(session, turn, text)];

    public bool RejectsResume { get; init; }

    public bool RefusesToStart { get; init; }

    public Action<SessionOptions> Launching { get; init; } = _ => { };

    public Func<Option<AgentAccount>> Account { get; init; } = () => Option<AgentAccount>.None;

    public IReadOnlyList<ScriptedSession> Sessions => [.. sessions];

    public static IEnumerable<IAgentEvent> Reply(SessionId session, TurnId turn) =>
    [
        new TurnStarted(session, turn),
        new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"),
        new ItemProgressed(session, turn, new ItemId("reply"), "Done."),
        new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded),
        new TurnCompleted(session, turn, TurnOutcome.Finished),
    ];

    public CapabilitySet CapabilitiesOn(ConnectionEnvironment connection) => OnConnection(connection, Capabilities);

    public ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken)
    {
        if (RefusesToStart || (options.Resume.IsSome && RejectsResume))
        {
            return ValueTask.FromResult(Result<IAgentSession, AgentError>.Failure(AgentError.CannotResume));
        }

        Launching(options);
        var session = new ScriptedSession(script, options, Account) { MidTurn = MidTurn };
        sessions.Enqueue(session);

        return ValueTask.FromResult(Result<IAgentSession, AgentError>.Success(session));
    }
}
