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

    [Fact]
    public async Task TheAuditOfAGovernedJobShowsTheSameDecisionsDenialsAssumptionsAndAutonomyAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "governed", (".avala/permissions.json", GovernedPolicy));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await AuditAsync(run);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Contains(before, line => line.StartsWith("audit: 1 denied · ", StringComparison.Ordinal) && line.EndsWith("1 assumption", StringComparison.Ordinal));
        Assert.Contains("decision: Denied Command dotnet ef database update · rule no-migrations", before);
        Assert.Contains("assumption: Which database should the service use?: PostgreSQL", before);
        Assert.Contains("autonomy: Autonomous · Autonomous, as the repository declares", before);
        Assert.Contains(before, line => line.StartsWith("exception: Denied: run dotnet ef database update", StringComparison.Ordinal));
        Assert.Equal(before, await AuditAsync(run));
    }

    private const string GovernedPolicy = """
        { "autonomy": "autonomous", "rules": [ { "name": "no-migrations", "kind": "command", "target": "dotnet ef*", "answer": "deny" } ] }
        """;

    private static async Task<IReadOnlyList<string>> AuditAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        var sections = await workbench.InspectAsync(run.Job, "AuditSectionViewModel", "AutonomySectionViewModel");
        var (audit, autonomy) = (sections[0], sections[1]);
        var review = await ReviewAsync(run, workbench);

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        [
            $"audit: {audit["Fact"].Text} · {audit["Summary"].Text}",
            .. audit["Decisions"].Value<IReadOnlyList<string>>().Select(decision => $"decision: {decision}"),
            .. audit["Assumptions"].Value<IReadOnlyList<string>>().Select(assumption => $"assumption: {assumption}"),
            $"autonomy: {autonomy["Fact"].Text} · {autonomy["Autonomy"].Text}",
            .. review["Exceptions"].Items.Select(exception => $"exception: {exception["Title"].Text} | {exception["Fact"].Text}"),
        ]);
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
