using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;
using Avala.Supervision.Watching;

namespace Avala.Supervision.Supervising;

internal sealed class Watchdog(SupervisionBook book, SilenceAlarms alarms, ISupervisionSettings settings, Intervener intervener)
    : IHandle<JobProgressed>, IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<SilenceNoticed>
{
    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await KeepAsync(book.Watch(integrationEvent.Job).Progressed(integrationEvent.Status, alarms.Now), cancellationToken);

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        book.Tie(integrationEvent.Session, integrationEvent.Job);
        book.Keep(book.Watch(integrationEvent.Job).Joined(integrationEvent.Session));

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken) =>
        await book.JobOf(integrationEvent.Event.Session).Match(
            job => KeepAsync(book.Watch(job).Saw(integrationEvent.Event, alarms.Now), cancellationToken),
            () => Task.CompletedTask);

    public async ValueTask HandleAsync(SilenceNoticed integrationEvent, CancellationToken cancellationToken)
    {
        await alarms.RangAsync(integrationEvent.Job);

        var window = await WindowAsync(cancellationToken);
        var watch = book.Watch(integrationEvent.Job);

        await watch.SilenceAt(alarms.Now, window).Match(
            silent => intervener.StalledAsync(watch.Job, new SilenceMeasure(silent, window), cancellationToken),
            () => ArmAsync(watch, cancellationToken));
    }

    private async Task KeepAsync(JobWatch watch, CancellationToken cancellationToken)
    {
        book.Keep(watch);
        await ArmAsync(watch, cancellationToken);
    }

    private async Task ArmAsync(JobWatch watch, CancellationToken cancellationToken)
    {
        if (watch.IsArmed)
        {
            alarms.Set(watch.Job, watch.Due(await WindowAsync(cancellationToken)));
        }
    }

    private async Task<TimeSpan> WindowAsync(CancellationToken cancellationToken) => (await settings.LoadAsync(cancellationToken)).Silence;
}
