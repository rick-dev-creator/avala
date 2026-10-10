using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Triggers.Contracts;
using Avala.Workbench.Automation;
using Avala.Workbench.Triggers;

namespace Avala.Workbench.Tests.Triggers;

public sealed class TriggerItemViewModelScripts
{
    private static readonly TriggerState Interval =
        new(FakeTriggers.Nightly, "/work/ledger-api", TriggerKind.Interval, TriggerTarget.Loop, Autonomy.Autonomous, 2, Enabled: true)
        {
            EveryMinutes = 720,
            NextRun = new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.Zero),
            Running = 1,
        };

    [Fact]
    public void AnEnabledTriggerSaysWhatFiresItWhenItRunsNextWhatItStartsAndItsLatestRunsFirst() =>
        ViewModelScript.Given(new TriggerItemViewModel(
                new TriggerView(Interval, [Run(RunOutcome.Enqueued, FakeTriggers.Now.AddHours(1)), Run(RunOutcome.Submitted, FakeTriggers.Now)]),
                TimeZoneInfo.Utc))
            .Then(item =>
            {
                Assert.Equal(
                    (FakeTriggers.Nightly, "nightly", "Every 12 hours", "Next · Fri 9 Oct 18:00", "1 of 2 running", "Queues a loop task · autonomous at most", true, "Disable"),
                    (item.Id, item.Name, item.Fires, item.Next, item.Running, item.Target, item.IsEnabled, item.Toggle));
                Assert.Equal(["2026-10-09 14:00 · on schedule · queued in the loop", "2026-10-09 13:00 · on schedule · job submitted"], item.Runs);
            });

    [Fact]
    public void ADisabledTriggerSaysSoAndOffersToEnableIt() =>
        ViewModelScript.Given(new TriggerItemViewModel(new TriggerView(Interval with { Enabled = false }, []), TimeZoneInfo.Utc))
            .Then(item => Assert.Equal(("Disabled", "Enable", false, 0), (item.Next, item.Toggle, item.IsEnabled, item.Runs.Count)));

    [Fact]
    public void AWebhookTriggerRunsOnEachSignedDelivery() =>
        ViewModelScript.Given(new TriggerItemViewModel(new TriggerView(Interval with { Kind = TriggerKind.Webhook, Hook = "/hooks/nightly" }, []), TimeZoneInfo.Utc))
            .Then(item => Assert.Equal(("Webhook · /hooks/nightly", "Runs on each signed delivery"), (item.Fires, item.Next)));

    private static TriggerRun Run(RunOutcome outcome, DateTimeOffset at) =>
        new(Guid.NewGuid(), FakeTriggers.Nightly, TriggerOrigin.Schedule, "schedule", at, outcome);
}
