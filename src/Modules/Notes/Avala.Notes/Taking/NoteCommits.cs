using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Notes.Taking;

internal sealed class NoteCommits(IWorkingCommits commits)
    : IHandle<NoteTaken>, IHandle<NoteChanged>, IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    private readonly Dictionary<SessionId, JobId> jobs = [];

    private readonly HashSet<SessionId> working = [];

    private readonly Dictionary<string, NoteRecord> waiting = [];

    public ValueTask HandleAsync(NoteTaken integrationEvent, CancellationToken cancellationToken) => KeptAsync(integrationEvent.Note, cancellationToken);

    public ValueTask HandleAsync(NoteChanged integrationEvent, CancellationToken cancellationToken) => KeptAsync(integrationEvent.Note, cancellationToken);

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        jobs[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        switch (integrationEvent.Event)
        {
            case TurnStarted started:
                working.Add(started.Session);
                break;
            case TurnCompleted completed:
                await SettledAsync(completed.Session, cancellationToken);
                break;
        }
    }

    private async ValueTask KeptAsync(NoteRecord note, CancellationToken cancellationToken)
    {
        if (note.Place.Job.Match(Working, () => false))
        {
            waiting[Key(note)] = note;

            return;
        }

        await CommittedAsync(note, cancellationToken);
    }

    private async Task SettledAsync(SessionId session, CancellationToken cancellationToken)
    {
        working.Remove(session);

        if (!jobs.TryGetValue(session, out var job) || Working(job))
        {
            return;
        }

        foreach (var note in waiting.Values.Where(note => note.Place.Job == Option<JobId>.Some(job)).ToList())
        {
            waiting.Remove(Key(note));
            await CommittedAsync(note, cancellationToken);
        }
    }

    private bool Working(JobId job) => working.Any(session => jobs.TryGetValue(session, out var running) && running == job);

    private async Task CommittedAsync(NoteRecord note, CancellationToken cancellationToken) =>
        _ = await commits.CommitAsync(note.Root, note.Folder, $"Note: {note.Title}", cancellationToken);

    private static string Key(NoteRecord note) => $"{note.Root}|{note.Folder}";
}
