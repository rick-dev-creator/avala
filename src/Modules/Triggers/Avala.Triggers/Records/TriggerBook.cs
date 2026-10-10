using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;
using Avala.Triggers.Firing;
using Avala.Triggers.Receiving;
using Avala.Triggers.Scheduling;

namespace Avala.Triggers.Records;

internal interface IWebhookEndpoint
{
    WebhookEndpoint Current { get; }

    Task RefreshAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken);
}

internal sealed class TriggerBook(TriggerScheduler scheduler, TriggerFirer firer, FiringChecks checks, IWebhookEndpoint endpoint) : ITriggers
{
    public WebhookEndpoint Endpoint => endpoint.Current;

    public async ValueTask<IReadOnlyList<TriggerState>> ListAsync(CancellationToken cancellationToken)
    {
        var states = new List<TriggerState>();

        foreach (var trigger in scheduler.Current.Triggers)
        {
            states.Add(State(trigger) with { Running = await checks.RunningAsync(trigger.Id, cancellationToken) });
        }

        return states;
    }

    public IReadOnlyList<TriggerRun> RunsOf(TriggerId trigger) => firer.Journal.Of(trigger);

    public IReadOnlyList<WebhookDelivery> Deliveries() => firer.Journal.Deliveries;

    public IReadOnlyList<TriggerFile> Files() => scheduler.Current.Files;

    public async ValueTask<Result<TriggerRun, TriggerError>> FireAsync(TriggerId trigger, FireRequest request, CancellationToken cancellationToken) =>
        await firer.FireAsync(trigger, request, cancellationToken);

    public async ValueTask<Result<TriggerId, TriggerError>> EnableAsync(TriggerId trigger, bool enabled, CancellationToken cancellationToken) =>
        await scheduler.Current.Find(trigger).Match(
            async declared =>
            {
                await scheduler.EnableAsync(declared, enabled, cancellationToken);

                return Result<TriggerId, TriggerError>.Success(trigger);
            },
            () => Task.FromResult(Result<TriggerId, TriggerError>.Failure(TriggerError.UnknownTrigger)));

    public async ValueTask<IReadOnlyList<TriggerFile>> ReloadAsync(CancellationToken cancellationToken)
    {
        var snapshot = await scheduler.ReloadAsync(cancellationToken);
        await endpoint.RefreshAsync(snapshot, cancellationToken);

        return snapshot.Files;
    }

    private static Option<DateTimeOffset> Last(IReadOnlyList<TriggerRun> runs) => runs.Count == 0 ? Option<DateTimeOffset>.None : runs[^1].At;

    private TriggerState State(TriggerDeclaration trigger) =>
        new(trigger.Id, trigger.Repository, trigger.Kind, trigger.Target, trigger.Autonomy, trigger.Concurrency, scheduler.Enabled(trigger))
        {
            EveryMinutes = trigger.Schedule.Match(schedule => schedule.EveryMinutes, () => 0),
            At = trigger.Schedule.Bind(schedule => schedule.Kind == TriggerKind.FixedTime ? Option<TimeOnly>.Some(schedule.At) : Option<TimeOnly>.None),
            Days = trigger.Schedule.Match(schedule => schedule.Days, () => []),
            CatchUp = trigger.CatchUp,
            Hook = trigger.Hook,
            NextRun = scheduler.NextOf(trigger.Id),
            LastRun = Last(firer.Journal.Of(trigger.Id)),
        };
}
