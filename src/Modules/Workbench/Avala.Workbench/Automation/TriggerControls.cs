using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Workbench.Automation;

internal sealed record TriggerView(TriggerState State, IReadOnlyList<TriggerRun> Runs);

internal sealed record TriggersState(
    IReadOnlyList<TriggerView> Triggers,
    IReadOnlyList<TriggerFile> Files,
    IReadOnlyList<WebhookDelivery> Deliveries,
    WebhookEndpoint Endpoint,
    TimeZoneInfo Zone);

internal sealed class TriggerControls(ITriggers triggers, TimeProvider clock)
{
    public const int RunsShown = 5;

    public const int DeliveriesShown = 20;

    public const string Person = "person";

    public async ValueTask<TriggersState> ReadAsync(CancellationToken cancellationToken)
    {
        var listed = await triggers.ListAsync(cancellationToken);

        return new TriggersState(
            [.. listed.Select(state => new TriggerView(state, [.. triggers.RunsOf(state.Id).TakeLast(RunsShown).Reverse()]))],
            triggers.Files(),
            [.. triggers.Deliveries().TakeLast(DeliveriesShown).Reverse()],
            triggers.Endpoint,
            clock.LocalTimeZone);
    }

    public ValueTask<Result<TriggerRun, TriggerError>> RunNowAsync(TriggerId trigger, CancellationToken cancellationToken) =>
        triggers.FireAsync(trigger, new FireRequest(TriggerOrigin.Manual, Person), cancellationToken);

    public ValueTask<Result<TriggerId, TriggerError>> EnableAsync(TriggerId trigger, bool enabled, CancellationToken cancellationToken) =>
        triggers.EnableAsync(trigger, enabled, cancellationToken);

    public ValueTask<IReadOnlyList<TriggerFile>> ReloadAsync(CancellationToken cancellationToken) => triggers.ReloadAsync(cancellationToken);
}
