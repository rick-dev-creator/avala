using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Workbench.Tests;

internal sealed class FakeTriggers : ITriggers
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 13, 0, 0, TimeSpan.Zero);

    public static readonly TriggerId Nightly = new("/work/ledger-api", "nightly");

    public List<(TriggerId Trigger, TriggerOrigin Origin, string Who)> Fired { get; } = [];

    public List<(TriggerId Trigger, bool Enabled)> Enabled { get; } = [];

    public Option<TriggerError> Refusal { get; set; }

    public int Reloads { get; private set; }

    public WebhookEndpoint Endpoint { get; } = new("http://localhost:24010/hooks/", Option<TriggerError>.None);

    public ValueTask<IReadOnlyList<TriggerState>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TriggerState>>(
        [
            new TriggerState(Nightly, "/work/ledger-api", TriggerKind.FixedTime, TriggerTarget.Job, Autonomy.Supervised, 1, Enabled: true)
            {
                At = new TimeOnly(2, 30),
                Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
                NextRun = new DateTimeOffset(2026, 10, 12, 0, 30, 0, TimeSpan.Zero),
                Running = 1,
            },
            new TriggerState(new TriggerId(TriggerId.Machine, "issue"), "/work/ledger-api", TriggerKind.Webhook, TriggerTarget.Job, Autonomy.Autonomous, 2, Enabled: false) { Hook = "/hooks/issue" },
        ]);

    public IReadOnlyList<TriggerRun> RunsOf(TriggerId trigger) =>
        trigger == Nightly
            ? [new TriggerRun(Guid.NewGuid(), trigger, TriggerOrigin.Manual, "person", Now, RunOutcome.Submitted) { Job = JobId.New(), Asked = Autonomy.Autonomous, Applied = Autonomy.Supervised }]
            : [];

    public IReadOnlyList<WebhookDelivery> Deliveries() => [new WebhookDelivery(Guid.NewGuid(), Now, "/hooks/issue", DeliveryVerdict.BadSignature)];

    public IReadOnlyList<TriggerFile> Files() => [new TriggerFile("/data/triggers.json", TriggerFileStatus.Applied) { Triggers = 2 }];

    public ValueTask<Result<TriggerRun, TriggerError>> FireAsync(TriggerId trigger, FireRequest request, CancellationToken cancellationToken)
    {
        Fired.Add((trigger, request.Origin, request.Who));

        return ValueTask.FromResult(Result<TriggerRun, TriggerError>.Success(new TriggerRun(Guid.NewGuid(), trigger, request.Origin, request.Who, Now, RunOutcome.Submitted)));
    }

    public ValueTask<Result<TriggerId, TriggerError>> EnableAsync(TriggerId trigger, bool enabled, CancellationToken cancellationToken)
    {
        Enabled.Add((trigger, enabled));

        return ValueTask.FromResult(Refusal.Match(Result<TriggerId, TriggerError>.Failure, () => Result<TriggerId, TriggerError>.Success(trigger)));
    }

    public ValueTask<IReadOnlyList<TriggerFile>> ReloadAsync(CancellationToken cancellationToken)
    {
        Reloads++;

        return ValueTask.FromResult(Files());
    }
}
