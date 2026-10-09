using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class ScreenHonestyTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AVerifiedJobShowsTheSameVerdictChecksAndTailsAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "fix-after-feedback",
            (".avala/checks.json", ReviewTests.CalculatorChecks),
            (".avala/permissions.json", ReviewTests.TestsPolicy));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await EvidenceAsync(run);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Contains("verdict: Verified on attempt 2 of 2", before);
        Assert.Contains(before, line => line.StartsWith("attempt: Attempt 1: failed · calculator failed (exit 1, ", StringComparison.Ordinal));
        Assert.Contains(before, line => line.StartsWith("exception: Attempt 1 failed | calculator · exit 1 | ", StringComparison.Ordinal));
        Assert.Equal(before, await EvidenceAsync(run));
    }

    private static async Task<IReadOnlyList<string>> EvidenceAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        var (group, fact) = await workbench.RowAsync(run.Job);
        var evidence = Assert.Single(await workbench.InspectAsync(run.Job, "EvidenceSectionViewModel"));
        var review = await ReviewAsync(run, workbench);

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        [
            $"row: {group} · {fact}",
            $"evidence: {evidence["Fact"].Text} · {evidence["Summary"].Text}",
            .. evidence["Attempts"].Value<IReadOnlyList<string>>().Select(attempt => $"attempt: {attempt}"),
            $"verdict: {review["Verdict"].Text}",
            .. review["Exceptions"].Items.Select(exception => $"exception: {exception["Title"].Text} | {exception["Fact"].Text} | {exception["Output"].Text}"),
        ]);
    }

    private static async Task<Bound> ReviewAsync(SimulatedRun run, OpenWorkbench workbench)
    {
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));

        return await run.Ui.ReadAsync(() => workbench.Page["Review"]);
    }
}
