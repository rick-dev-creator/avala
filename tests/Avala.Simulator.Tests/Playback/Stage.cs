using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Simulator.FileSystem;
using Avala.Simulator.Workloads;
using Avala.Simulator.Playback;
using Avala.Simulator.Recordings;
using Avala.Simulator.Scenarios;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

internal sealed class Stage : IAsyncDisposable
{
    public static readonly HarnessTool CanvasTool = new("canvas", "Draw a canvas", "{}", ToolSurface.Canvas);

    private readonly TemporaryFolder folder = new();
    private readonly TemporaryFolder data = new();
    private bool sessionClosed;

    public Stage(PermissionMode permissions = PermissionMode.AskEveryTime, params HarnessTool[] tools)
        : this(UncontainedProcesses.Instance, permissions, tools)
    {
    }

    public Stage(IProcessLauncher processes, PermissionMode permissions, params HarnessTool[] tools)
    {
        Craft = Crafted(new AvalaPaths(data.Path));
        Session = new SimulatedSession(
            new SessionOptions(folder.Path, permissions) { Tools = tools, Processes = processes },
            Craft,
            SimulatedAccounts.Default,
            Option<Conversation>.None);
    }

    public Stagecraft Craft { get; }

    public string Recordings => Path.Combine(data.Path, RecordingFolder.FolderName);

    public static Stagecraft Crafted(AvalaPaths paths) =>
        new(new DiskFileWriter(), new Pacing(TimeProvider.System, TimeSpan.Zero), new ScenarioLibrary(new RecordingFolder(paths)), new DotnetWorkloads());

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
        await new SimulatedProvider(Craft)
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

    public async ValueTask CloseSessionAsync()
    {
        if (!sessionClosed)
        {
            sessionClosed = true;
            await Session.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseSessionAsync();
        await folder.DisposeAsync();
        await data.DisposeAsync();
    }
}
