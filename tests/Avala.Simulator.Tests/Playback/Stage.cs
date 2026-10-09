using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.FileSystem;
using Avala.Simulator.Playback;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

internal sealed class Stage : IAsyncDisposable
{
    private readonly TemporaryFolder folder = new();

    public Stage(PermissionMode permissions = PermissionMode.AskEveryTime) =>
        Session = new SimulatedSession(
            new SessionOptions(folder.Path, permissions),
            new DiskFileWriter(),
            new Pacing(TimeProvider.System, TimeSpan.Zero));

    public SimulatedSession Session { get; }

    public string WorkingDirectory => folder.Path;

    public async Task<TurnId> SendAsync(string message, CancellationToken cancellationToken) =>
        Outcomes.Succeeds(await Session.SendAsync(new UserTurn(message), cancellationToken));

    public Task<IReadOnlyList<IAgentEvent>> ReadTurnAsync(CancellationToken cancellationToken) =>
        ReadUntilAsync<TurnCompleted>(cancellationToken);

    public async Task<IReadOnlyList<IAgentEvent>> ReadUntilAsync<TEvent>(CancellationToken cancellationToken)
        where TEvent : IAgentEvent
    {
        var seen = new List<IAgentEvent>();

        await foreach (var agentEvent in Session.Events.WithCancellation(cancellationToken))
        {
            seen.Add(agentEvent);

            if (agentEvent is TEvent)
            {
                break;
            }
        }

        return seen;
    }

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
