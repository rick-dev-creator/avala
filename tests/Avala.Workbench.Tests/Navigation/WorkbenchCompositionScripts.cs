using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Workbench.Inspector;
using Avala.Workbench.Navigation;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Tests.Inspector;

namespace Avala.Workbench.Tests.Navigation;

public sealed class WorkbenchCompositionScripts : IDisposable
{
    private readonly Bench bench = new();
    private readonly SidebarViewModel sidebar;
    private readonly WorkbenchViewModel page;
    private readonly UsageSectionViewModel usage;
    private readonly WorktreeSectionViewModel worktree;

    public WorkbenchCompositionScripts()
    {
        sidebar = bench.Sidebar();
        page = bench.Workbench();
        usage = new UsageSectionViewModel(bench.Inspected());
        worktree = new WorktreeSectionViewModel(bench.Inspected());
        bench.Regions.Fill(ShellRegions.Sidebar, sidebar);
        bench.Regions.Fill(ShellRegions.Content, page);
        bench.Regions.Fill(ShellRegions.Inspector, usage, worktree);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobChosenInTheSidebarOpensOnThePageAndReachesEveryInspectorSection()
    {
        var job = InspectedJobs.Reviewed(bench);
        bench.Publish(Bench.OnBoard(job));
        await ActivateAsync();
        await bench.Ui.InvokeAsync(() => page.ToggleInspectorCommand.Execute(null), Cancellation);

        await worktree.PresentsAfterAsync(() => bench.Post(() => sidebar.SelectCommand.Execute(sidebar.ReadyForReview[0])), () => worktree.Branch, Cancellation);
        await bench.Ui.PresentedAsync(usage, () => usage.IsLoaded, () => usage.Spent);

        Assert.Equal(job.Job, await bench.Ui.ReadAsync(() => page.Conversation!.Job));
        Assert.Equal(("avala/fix-the-test", "USD 0.25 · 1,500 tokens"), await bench.Ui.ReadAsync(() => (worktree.Branch, usage.Spent)));
    }

    [Fact]
    public async Task UsageRecordedForTheInspectedJobShowsInTheInspectorWhileTheSidebarKeepsItsRow()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        await ActivateAsync();
        await bench.Ui.InvokeAsync(
            () =>
            {
                sidebar.SelectCommand.Execute(sidebar.Running[0]);
                page.ToggleInspectorCommand.Execute(null);
            },
            Cancellation);
        await bench.Ui.PresentedAsync(usage, () => usage.IsLoaded, () => usage.Spent);
        var row = await bench.Ui.ReadAsync(() => sidebar.Running[0]);
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);

        bench.Publish(Bench.OnBoard(job, revision: 1));

        await bench.Ui.PresentedAsync(usage, () => usage.Spent == "USD 0.25 · 1,500 tokens", () => usage.Spent);
        Assert.Same(row, await bench.Ui.ReadAsync(() => sidebar.Running[0]));
    }

    [Fact]
    public async Task ChoosingAnotherJobMovesEveryInspectorSectionToIt()
    {
        var first = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        var second = bench.Job("Add invoice PDF endpoint", JobStatus.Running);
        bench.Publish(Bench.OnBoard(first), Bench.OnBoard(second));
        await ActivateAsync();
        await bench.Ui.InvokeAsync(
            () =>
            {
                sidebar.SelectCommand.Execute(sidebar.Running.Single(row => row.Job == first.Job));
                page.ToggleInspectorCommand.Execute(null);
            },
            Cancellation);
        await bench.Ui.PresentedAsync(worktree, () => worktree.Path.EndsWith(first.Job.Value.ToString(), StringComparison.Ordinal), () => worktree.Path);

        await worktree.PresentsAfterAsync(() => bench.Post(() => sidebar.SelectCommand.Execute(sidebar.Running.Single(row => row.Job == second.Job))), () => worktree.Path, Cancellation);

        Assert.Equal($"/worktrees/{second.Job.Value}", await bench.Ui.ReadAsync(() => worktree.Path));
        Assert.Equal(second.Job, await bench.Ui.ReadAsync(() => page.Conversation!.Job));
    }

    [Fact]
    public async Task ClosingTheInspectorEmptiesEverySection()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        await ActivateAsync();
        await bench.Ui.InvokeAsync(
            () =>
            {
                sidebar.SelectCommand.Execute(sidebar.Running[0]);
                page.ToggleInspectorCommand.Execute(null);
            },
            Cancellation);
        await bench.Ui.PresentedAsync(worktree, () => worktree.IsLoaded, () => worktree.Path);

        await worktree.PresentsAfterAsync(() => bench.Post(() => page.ToggleInspectorCommand.Execute(null)), () => worktree.Path, Cancellation);

        Assert.Equal((false, false), await bench.Ui.ReadAsync(() => (worktree.IsLoaded, usage.IsLoaded)));
    }

    public void Dispose()
    {
        page.Dispose();
        sidebar.Dispose();
        usage.Dispose();
        worktree.Dispose();
        bench.Dispose();
    }

    private async Task ActivateAsync() =>
        await sidebar.PresentsAfterAsync(
            () => bench.Post(() =>
            {
                sidebar.Activate();
                usage.Activate();
                worktree.Activate();
                page.Activate();
            }),
            () => "the sidebar did not show",
            Cancellation);
}
