using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Host.Tests;

public sealed class UsageWindowTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task TheUsagePageShowsTheChosenWindowDayByDayWithTodaysTokensAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.UsageRecordedAsync(1);
        var usage = run.Page("Usage");
        await run.Ui.InvokeAsync(((IActivatable)usage.Target).Activate, TestContext.Current.CancellationToken);
        var range = await run.Ui.ReadAsync(() => usage["Range"]);

        await run.Ui.PresentedAsync(
            usage.Presentation,
            () => range["Days"].Items.Count == 7 && range["Days"].Items[0]["Tokens"].Text != "0 tokens",
            () => $"{range["Days"].Items.Count} days");
        var week = await run.Ui.ReadAsync(() => (range["Total"]["Tokens"].Text, range["Days"].Items[0]["Tokens"].Text, range["Days"].Items[0]["IsToday"].Value<bool>(), range["Days"].Items[1]["Tokens"].Text));

        await run.Ui.InvokeAsync(() => range.Execute("ShowTodayCommand"), TestContext.Current.CancellationToken);
        await run.Ui.PresentedAsync(usage.Presentation, () => range["Days"].Items.Count == 1, () => $"{range["Days"].Items.Count} days");

        Assert.Equal((week.Item2, true, "0 tokens"), (week.Item1, week.Item3, week.Item4));
        Assert.Equal(("Today", week.Item2), await run.Ui.ReadAsync(() => (range["Total"]["Label"].Text, range["Total"]["Tokens"].Text)));
    }
}
