using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;
using Avala.Supervision.Watching;

namespace Avala.Supervision.Supervising;

internal sealed class Watchdog(SilenceAlarms alarms, ISupervisionSettings settings, Intervener intervener)
    : IHandle<JobProgressed>, IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<SilenceNoticed>
{
    private readonly Dictionary<JobId, JobWatch> watches = [];
    private readonly Dictionary<SessionId, JobId> jobs = [];

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await KeepAsync(Watch(integrationEvent.Job).Progressed(integrationEvent.Status, alarms.Now), cancellationToken);

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        jobs[integrationEvent.Session] = integrationEvent.Job;
        watches[integrationEvent.Job] = Watch(integrationEvent.Job).Joined(integrationEvent.Session);

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (jobs.TryGetValue(integrationEvent.Event.Session, out var job))
        {
            await KeepAsync(Watch(job).Saw(integrationEvent.Event, alarms.Now), cancellationToken);
        }
    }

    public async ValueTask HandleAsync(SilenceNoticed integrationEvent, CancellationToken cancellationToken)
    {
        await alarms.RangAsync(integrationEvent.Job);

        var window = await WindowAsync(cancellationToken);
        var watch = Watch(integrationEvent.Job);

        await watch.SilenceAt(alarms.Now, window).Match(
            silent => intervener.StalledAsync(watch.Job, new SilenceMeasure(silent, window), cancellationToken),
            () => ArmAsync(watch, cancellationToken));
    }

    private JobWatch Watch(JobId job) => watches.GetValueOrDefault(job) ?? new JobWatch(job);

    private async Task KeepAsync(JobWatch watch, CancellationToken cancellationToken)
    {
        watches[watch.Job] = watch;
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
