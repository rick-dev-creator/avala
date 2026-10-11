using Avala.Autopilot.Contracts;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Workbench.Automation;

internal sealed record TriggerView(TriggerState State, IReadOnlyList<TriggerRun> Runs)
{
    public Option<ForgeEventWatch> Watch { get; init; }
}

internal sealed record TriggersState(
    IReadOnlyList<TriggerView> Triggers,
    IReadOnlyList<TriggerFile> Files,
    IReadOnlyList<WebhookDelivery> Deliveries,
    WebhookEndpoint Endpoint,
    IReadOnlyList<LoopDigest> Loops,
    TimeZoneInfo Zone)
{
    public IReadOnlyList<ForgeEventEntry> ForgeEvents { get; init; } = [];
}

internal sealed class TriggerControls(ITriggers triggers, LoopControls loops, TimeProvider clock, IEnumerable<IForgeEventLog> forges)
{
    public const int RunsShown = 5;

    public const int DeliveriesShown = 20;

    public const int ForgeEventsShown = 20;

    public const string Person = "person";

    public async ValueTask<TriggersState> ReadAsync(CancellationToken cancellationToken)
    {
        var listed = await triggers.ListAsync(cancellationToken);
        var log = forges.FirstOrDefault();
        var watches = log?.Watches() ?? [];

        return new TriggersState(
            [
                .. listed.Select(state => new TriggerView(state, [.. triggers.RunsOf(state.Id).TakeLast(RunsShown).Reverse()])
                {
                    Watch = watches.FirstOrDefault(watch => watch.Trigger == state.Id).ToOption(),
                }),
            ],
            triggers.Files(),
            [.. triggers.Deliveries().TakeLast(DeliveriesShown).Reverse()],
            triggers.Endpoint,
            loops.Read(),
            clock.LocalTimeZone)
        {
            ForgeEvents = [.. (log?.Events() ?? []).TakeLast(ForgeEventsShown).Reverse()],
        };
    }

    public ValueTask<Result<TriggerRun, TriggerError>> RunNowAsync(TriggerId trigger, CancellationToken cancellationToken) =>
        triggers.FireAsync(trigger, new FireRequest(TriggerOrigin.Manual, Person), cancellationToken);

    public async ValueTask<Result<ForgeEventWatch, ForgeError>> CheckNowAsync(TriggerId trigger, CancellationToken cancellationToken) =>
        forges.FirstOrDefault() is { } log ? await log.CheckNowAsync(trigger, cancellationToken) : ForgeError.EventsUnsupported;

    public ValueTask<Result<TriggerId, TriggerError>> EnableAsync(TriggerId trigger, bool enabled, CancellationToken cancellationToken) =>
        triggers.EnableAsync(trigger, enabled, cancellationToken);

    public ValueTask<IReadOnlyList<TriggerFile>> ReloadAsync(CancellationToken cancellationToken) => triggers.ReloadAsync(cancellationToken);
}
