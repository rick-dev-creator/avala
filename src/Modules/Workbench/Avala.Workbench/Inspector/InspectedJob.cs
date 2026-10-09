using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Inspection;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Inspector;

internal sealed class InspectedJob(BoardFeed feed, InspectedFacts facts) : IPresentation, IDisposable
{
    private Action<Option<InspectorFacts>> show = _ => { };
    private Option<JobId> focus;
    private int requested = -1;

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public Task Following => feed.Following;

    public void Showing(Action<Option<InspectorFacts>> shown) => show = shown;

    public void Focus(Option<JobId> job)
    {
        focus = job;
        requested = -1;

        if (job.IsNone)
        {
            Apply(Option<InspectorFacts>.None);

            return;
        }

        Loading = feed.IsActive && Requested(feed.Jobs) is [var load] ? load(CancellationToken.None) : Task.CompletedTask;
    }

    public void Activate() => feed.Start(Requested);

    public void Deactivate()
    {
        feed.Stop();
        requested = -1;
    }

    public void Dispose() => Deactivate();

    private IReadOnlyList<Func<CancellationToken, Task>> Requested(ImmutableDictionary<JobId, BoardJob> jobs) =>
        focus.Match<IReadOnlyList<Func<CancellationToken, Task>>>(
            job =>
            {
                var revision = jobs.TryGetValue(job, out var found) ? found.Revision : 0;

                if (revision == requested)
                {
                    return [];
                }

                requested = revision;

                return [_ => feed.LoadAsync(cancellationToken => facts.ReadAsync(job, revision, cancellationToken), read => ApplyIfCurrent(job, revision, read))];
            },
            () => []);

    private void ApplyIfCurrent(JobId job, int revision, Option<InspectorFacts> read)
    {
        if (focus == Option<JobId>.Some(job) && requested == revision)
        {
            Apply(read);
        }
    }

    private void Apply(Option<InspectorFacts> read)
    {
        show(read);
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }
}
