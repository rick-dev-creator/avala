using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.FileSystem;
using Avala.Simulator.Playback;
using Avala.Simulator.Scenarios;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

internal sealed class Stage : IAsyncDisposable
{
    public static readonly HarnessTool CanvasTool = new("canvas", "Draw a canvas", "{}", ToolSurface.Canvas);

    private readonly TemporaryFolder folder = new();

    public Stage(PermissionMode permissions = PermissionMode.AskEveryTime, params HarnessTool[] tools) =>
        Session = new SimulatedSession(
            new SessionOptions(folder.Path, permissions) { Tools = tools },
            new DiskFileWriter(),
            new Pacing(TimeProvider.System, TimeSpan.Zero),
            Option<Conversation>.None);

    public SimulatedSession Session { get; }

    public string WorkingDirectory => folder.Path;

    public async Task<TurnId> SendAsync(string message, CancellationToken cancellationToken) =>
        Outcomes.Succeeds(await Session.SendAsync(new UserTurn(message), cancellationToken));

    public Task<IReadOnlyList<IAgentEvent>> ReadTurnAsync(CancellationToken cancellationToken) =>
        ReadUntilAsync<TurnCompleted>(cancellationToken);

    public Task<IReadOnlyList<IAgentEvent>> ReadUntilAsync<TEvent>(CancellationToken cancellationToken)
        where TEvent : IAgentEvent =>
        ReadUntilAsync<TEvent>(Session, cancellationToken);

    public static async Task<IReadOnlyList<IAgentEvent>> ReadUntilAsync<TEvent>(IAgentSession session, CancellationToken cancellationToken)
        where TEvent : IAgentEvent
    {
        var seen = new List<IAgentEvent>();

        await foreach (var agentEvent in session.Events.WithCancellation(cancellationToken))
        {
            seen.Add(agentEvent);

            if (agentEvent is TEvent)
            {
                break;
            }
        }

        return seen;
    }

    public async Task<Result<IAgentSession, AgentError>> ResumeAsync(ResumeToken token, CancellationToken cancellationToken) =>
        await new SimulatedProvider(new DiskFileWriter(), new Pacing(TimeProvider.System, TimeSpan.Zero))
            .StartAsync(new SessionOptions(folder.Path, PermissionMode.AllowAll) { Resume = token }, cancellationToken);

    public async Task<IReadOnlyList<IAgentEvent>> ReadTurnAllowingEveryRequestAsync(CancellationToken cancellationToken)
    {
        var seen = new List<IAgentEvent>();

        await foreach (var agentEvent in Session.Events.WithCancellation(cancellationToken))
        {
            seen.Add(agentEvent);

            if (agentEvent is PermissionRequested requested)
            {
                Outcomes.Succeeds(await Session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Allow), cancellationToken));
            }

            if (agentEvent is TurnCompleted)
            {
                break;
            }
        }

        return seen;
    }

    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        folder.Dispose();
    }
}
