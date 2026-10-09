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

    [Fact]
    public async Task AJobsCapNearLimitAlertAndAccountShowTheSameOnUsageOverviewAndInspectorAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(
            plugins,
            "spent-window",
            (".avala/budget.json", """{ "costPerJob": { "USD": 0.065 }, "holdAtLimit": 0.9 }"""));
        Assert.Equal(HoldReason.LimitNearlyReached, (await run.BudgetInterventionAsync()).Hold.Reason);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var before = await SpendingAsync(run);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Contains("job: 0.06 USD of 0.065 USD · near cap True · 0.065 USD per job, holds at 90% of a limit · on simulator", before);
        Assert.Contains("limit: 5h 95% · Jobs on this connection hold at the 90% threshold. · reaches hold True", before);
        Assert.Contains("card: simulator · Simulated account · 5h · 95% · near limit True", before);
        Assert.Contains(before, line => line.StartsWith("inspector: ", StringComparison.Ordinal) && line.Contains("Cost cap", StringComparison.Ordinal));
        Assert.Equal(before, await SpendingAsync(run));
    }

    private static async Task<IReadOnlyList<string>> SpendingAsync(SimulatedRun run)
    {
        var usage = await ActivatedAsync(run, "Usage");
        await run.Ui.PresentedAsync(
            usage.Presentation,
            () => usage["Jobs"].Items.Count == 1 && usage["Connections"].Items.Count == 1 && usage["Connections"].Items[0]["Limits"].Items.Count == 1,
            () => $"jobs {usage["Jobs"].Items.Count}, connections {usage["Connections"].Items.Count}");
        var overview = (await ActivatedAsync(run, "Overview"))["Connections"];
        await run.Ui.PresentedAsync(
            overview.Presentation,
            () => overview["Connections"].Items.Count == 1,
            () => $"cards {overview["Connections"].Items.Count}");
        var workbench = await run.WorkbenchAsync();
        await workbench.ShowsInGroupAsync(run.Job, "NeedsYou");
        var inspector = Assert.Single(await workbench.InspectAsync(run.Job, "UsageSectionViewModel"));

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() =>
        {
            var (job, limit, card) = (usage["Jobs"].Items[0], usage["Connections"].Items[0]["Limits"].Items[0], overview["Connections"].Items[0]);

            return
            [
                $"job: {job["Cost"].Text} of {job["Cap"].Text} · near cap {job["IsNearCap"].Text} · {job["Caps"].Text} · on {job["Connection"].Text}",
                $"limit: {limit["Window"].Text} {limit["UsedText"].Text} · {limit["HoldAt"].Text} · reaches hold {limit["ReachesHold"].Text}",
                $"card: {card["Name"].Text} · {card["Account"].Text} · {card["Use"].Text} · near limit {card["IsNearLimit"].Text}",
                $"inspector: {inspector["Spent"].Text} · {string.Join(", ", inspector["Caps"].Value<IReadOnlyList<string>>())} · {string.Join(", ", inspector["Interventions"].Value<IReadOnlyList<string>>())}",
            ];
        });
    }

    private static async Task<Bound> ActivatedAsync(SimulatedRun run, string title)
    {
        var page = run.Page(title);
        await run.Ui.RunAsync(() =>
        {
            ((Avala.Sdk.IActivatable)page.Target).Activate();

            return page.Has("Loading") ? page["Loading"].Value<Task>() : Task.CompletedTask;
        });

        return page;
    }

    private const string GovernedPolicy = """
        { "autonomy": "autonomous", "rules": [ { "name": "no-migrations", "kind": "command", "target": "dotnet ef*", "answer": "deny" } ] }
        """;

    private static async Task<IReadOnlyList<string>> AuditAsync(SimulatedRun run)
    {
        var workbench = await run.WorkbenchAsync();
        await workbench.ShowsInGroupAsync(run.Job, "ReadyForReview");
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
        var (group, fact) = await workbench.RowAsync(run.Job, "ReadyForReview");
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
