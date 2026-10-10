using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;
using Avala.Workbench.Automation;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Triggers;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Triggers;

public sealed class TriggersViewModelScripts : IDisposable
{
    private static readonly DateTimeOffset Now = FakeTriggers.Now;

    private static readonly TriggerId Nightly = FakeTriggers.Nightly;

    private readonly TestUiDispatcher ui = new();
    private readonly FakeTriggers triggers = new();

    [Fact]
    public async Task ThePageListsEachTriggerWithWhatFiresItItsNextRunItsRunsTheFilesTheEndpointAndTheDeliveriesAsync()
    {
        using var page = Page();

        page.Activate();

        await ui.PresentedAsync(page, () => page.Triggers.Count == 2, () => $"{page.Triggers.Count} triggers");
        var (nightly, hook) = await ui.ReadAsync(() => (page.Triggers[0], page.Triggers[1]));
        Assert.Equal(
            ("nightly", "/work/ledger-api", "Weekdays at 02:30", "Next · Mon 12 Oct 02:30", "1 of 1 running", "Starts a job · supervised at most", "Disable"),
            (nightly.Name, nightly.Repository, nightly.Fires, nightly.Next, nightly.Running, nightly.Target, nightly.Toggle));
        Assert.Equal(["2026-10-09 15:00 · run by you · job submitted · capped to supervised by the repository"], nightly.Runs);
        Assert.Equal(("Webhook · /hooks/issue", "Disabled", "Enable", false), (hook.Fires, hook.Next, hook.Toggle, hook.IsEnabled));
        Assert.Equal(
            ("Times in this computer's time zone, Test/Madrid", "Listening on http://localhost:24010/hooks/<id>, on this computer only."),
            await ui.ReadAsync(() => (page.Scope, page.Endpoint)));
        Assert.Equal(["/data/triggers.json · applied, 2 triggers"], await ui.ReadAsync(() => page.Files.ToList()));
        Assert.Equal(["2026-10-09 15:00:00 · /hooks/issue · bad signature"], await ui.ReadAsync(() => page.Deliveries.ToList()));
        Assert.Contains("Tailscale Funnel", page.Tunnel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunNowFiresTheTriggerAsAPersonAndSaysWhatHappenedAsync()
    {
        using var page = Page();
        page.Activate();
        await ui.PresentedAsync(page, () => page.Triggers.Count == 2, () => $"{page.Triggers.Count} triggers");

        await ui.RunAsync(() => page.RunNowCommand.ExecuteAsync(page.Triggers[0]));

        Assert.Equal([(Nightly, TriggerOrigin.Manual, "person")], triggers.Fired);
        Assert.Equal(("Run now: job submitted.", string.Empty), await ui.ReadAsync(() => (page.Notice, page.Error)));
    }

    [Fact]
    public async Task ToggleDisablesAnEnabledTriggerAndEnablesADisabledOneAndARefusalShowsItsErrorAsync()
    {
        using var page = Page();
        page.Activate();
        await ui.PresentedAsync(page, () => page.Triggers.Count == 2, () => $"{page.Triggers.Count} triggers");
        var (nightly, hook) = await ui.ReadAsync(() => (page.Triggers[0], page.Triggers[1]));

        await ui.RunAsync(() => page.ToggleCommand.ExecuteAsync(nightly));
        triggers.Refusal = TriggerError.UnknownTrigger;
        await ui.RunAsync(() => page.ToggleCommand.ExecuteAsync(hook));

        Assert.Equal([(Nightly, false), (hook.Id, true)], triggers.Enabled);
        Assert.Equal("That trigger is no longer declared. Reload to see the current ones.", await ui.ReadAsync(() => page.Error));
        Assert.False(page.ToggleCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReloadReadsTheFilesAgainAsync()
    {
        using var page = Page();

        await ui.RunAsync(() => page.ReloadCommand.ExecuteAsync(null));

        Assert.Equal((1, "Reloaded the trigger files."), (triggers.Reloads, page.Notice));
    }

    public void Dispose() => ui.Dispose();

    private TriggersViewModel Page()
    {
        var clock = new FakeTimeProvider(Now);
        clock.SetLocalTimeZone(TriggerZones.Madrid);

        return new TriggersViewModel(new TriggerControls(triggers, clock), new LiveFeed(new Pulse(new JobBoard()), ui));
    }
}

internal static class TriggerZones
{
    public static TimeZoneInfo Madrid { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test/Madrid",
        TimeSpan.FromHours(1),
        "Test/Madrid",
        "CET",
        "CEST",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                new DateTime(2099, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0, DateTimeKind.Unspecified), 10, 5, DayOfWeek.Sunday)),
        ]);
}
