using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Workbench.Board;

internal sealed record BoardRemote(bool Offered, RemoteControl Control, Option<bool> Wanted, int Revision)
{
    public static BoardRemote None { get; } = new(false, RemoteControl.Off, Option<bool>.None, 0);
}

internal sealed class RemoteKeeper(JobBoard board) :
    IHandle<SessionOpened>,
    IHandle<JobSessionStarted>,
    IHandle<RemoteControlReported>,
    IHandle<JobRemoteControlChosen>
{
    private readonly HashSet<SessionId> offered = [];
    private readonly Dictionary<SessionId, RemoteControl> controls = [];
    private readonly Dictionary<SessionId, JobId> sessions = [];
    private ImmutableDictionary<JobId, BoardRemote> remotes = ImmutableDictionary<JobId, BoardRemote>.Empty;

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Capabilities.Has<ControlsRemotely>())
        {
            offered.Add(integrationEvent.Session);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        return KeepAsync(integrationEvent.Job, remote => remote with
        {
            Offered = offered.Contains(integrationEvent.Session),
            Control = controls.GetValueOrDefault(integrationEvent.Session, RemoteControl.Off),
        });
    }

    public ValueTask HandleAsync(RemoteControlReported integrationEvent, CancellationToken cancellationToken)
    {
        controls[integrationEvent.Session] = integrationEvent.Control;

        return sessions.TryGetValue(integrationEvent.Session, out var job)
            ? KeepAsync(job, remote => remote with { Control = integrationEvent.Control })
            : ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobRemoteControlChosen integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(integrationEvent.Job, remote => remote with { Wanted = integrationEvent.On });

    private ValueTask KeepAsync(JobId job, Func<BoardRemote, BoardRemote> change)
    {
        var kept = change(remotes.GetValueOrDefault(job, BoardRemote.None));
        remotes = remotes.SetItem(job, kept with { Revision = kept.Revision + 1 });
        board.Publish(remotes);

        return ValueTask.CompletedTask;
    }
}
