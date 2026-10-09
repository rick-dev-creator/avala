using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Navigation;

public sealed class WorkbenchViewModelTests : IDisposable
{
    private readonly Bench bench = new();
    private readonly WorkbenchViewModel workbench;

    public WorkbenchViewModelTests() => workbench = bench.Workbench();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhileActiveTheSidebarFollowsTheBoardAndSelectingAJobOpensItsConversation()
    {
        var summary = new FakeCatalog().Add("Fix the failing test", JobStatus.Running).Summary;
        workbench.Activate();

        bench.Board.Publish(Bench.Of(new BoardJob(summary, Transcript.Empty.WithPrompts(summary.Instruction, []))));
        await bench.Ui.UntilAsync(() => workbench.Sidebar.Running.Count == 1);
        await bench.Ui.InvokeAsync(() => workbench.Sidebar.SelectCommand.Execute(workbench.Sidebar.Running[0]), Cancellation);

        var conversation = await bench.Ui.ReadAsync(() => workbench.Conversation);
        Assert.Equal((summary.Job, "Fix the failing test"), (conversation?.Job, conversation?.Title));
        Assert.IsType<PromptViewModel>(Assert.Single(conversation!.Entries));
    }

    [Fact]
    public async Task AfterDeactivationTheBoardIsNoLongerFollowed()
    {
        var summary = new FakeCatalog().Add("Fix the failing test", JobStatus.Running).Summary;
        workbench.Activate();
        workbench.Deactivate();
        await workbench.Following;

        bench.Board.Publish(Bench.Of(new BoardJob(summary, Transcript.Empty)));

        Assert.Empty(await bench.Ui.ReadAsync(() => workbench.Sidebar.Running));
    }

    [Fact]
    public void TheInspectorIsClosedUntilToggled()
    {
        var closed = workbench.IsInspectorOpen;

        workbench.ToggleInspectorCommand.Execute(null);

        Assert.Equal((false, true), (closed, workbench.IsInspectorOpen));
    }

    [Fact]
    public async Task AReviewOpensOnlyForAJobAwaitingReviewOrHeldAndClosesWhenAnotherJobIsSelected()
    {
        var reviewed = bench.Job("Fix the failing test", JobStatus.AwaitingReview);
        var running = bench.Job("Add an endpoint", JobStatus.Running);
        workbench.Activate();
        bench.Publish(Bench.OnBoard(reviewed), Bench.OnBoard(running));
        await bench.Ui.UntilAsync(() => workbench.Sidebar.ReadyForReview.Count == 1 && workbench.Sidebar.Running.Count == 1);

        await bench.Ui.InvokeAsync(() => workbench.Sidebar.SelectCommand.Execute(workbench.Sidebar.ReadyForReview[0]), Cancellation);
        await bench.Ui.InvokeAsync(() => workbench.OpenReviewCommand.Execute(null), Cancellation);
        await bench.Ui.UntilAsync(() => workbench.Review is { IsLoaded: true });
        var opened = await bench.Ui.ReadAsync(() => workbench.Review?.Job);
        await bench.Ui.InvokeAsync(() => workbench.Sidebar.SelectCommand.Execute(workbench.Sidebar.Running[0]), Cancellation);

        Assert.Equal(
            (reviewed.Job, false, false),
            (opened, await bench.Ui.ReadAsync(() => workbench.Review is not null), await bench.Ui.ReadAsync(() => workbench.OpenReviewCommand.CanExecute(null))));
    }

    [Fact]
    public async Task TheOpenInspectorShowsTheSelectedJobAndReloadsWhenItsRevisionMoves()
    {
        var job = bench.Job("Fix the failing test", JobStatus.Running);
        workbench.Activate();
        bench.Publish(Bench.OnBoard(job));
        await bench.Ui.UntilAsync(() => workbench.Sidebar.Running.Count == 1);
        await bench.Ui.InvokeAsync(
            () =>
            {
                workbench.Sidebar.SelectCommand.Execute(workbench.Sidebar.Running[0]);
                workbench.ToggleInspectorCommand.Execute(null);
            },
            Cancellation);
        await bench.Ui.UntilAsync(() => workbench.Inspector is { IsLoaded: true });
        var before = await bench.Ui.ReadAsync(() => workbench.Inspector!.Usage.Spent);

        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(1200, 300, 0, 0, 0), [new Cost(0.25m, "USD")], 0, default, []);
        bench.Publish(Bench.OnBoard(job, revision: 1));

        await bench.Ui.UntilAsync(() => workbench.Inspector!.Usage.Spent == "USD 0.25 · 1,500 tokens");
        Assert.Equal("No usage reported", before);
    }

    [Fact]
    public async Task ALoadQueuedForTheUiBeforeTheWorkbenchWasDeactivatedIsNeverShown()
    {
        var hooked = new HookedDispatcher(bench.Ui);
        using var deactivated = bench.Workbench(hooked);
        var job = bench.Job("Fix the failing test", JobStatus.Running);
        deactivated.Activate();
        bench.Publish(Bench.OnBoard(job));
        await bench.Ui.UntilAsync(() => deactivated.Sidebar.Running.Count == 1);
        await bench.Ui.InvokeAsync(() => deactivated.Sidebar.SelectCommand.Execute(deactivated.Sidebar.Running[0]), Cancellation);
        hooked.BeforeNext = deactivated.Deactivate;

        await bench.Ui.InvokeAsync(() => deactivated.ToggleInspectorCommand.Execute(null), Cancellation);
        await (await bench.Ui.ReadAsync(() => deactivated.Loading));

        Assert.Equal((true, false), (hooked.Hooked, await bench.Ui.ReadAsync(() => deactivated.Inspector!.IsLoaded)));
    }

    private sealed class HookedDispatcher(IUiDispatcher inner) : IUiDispatcher
    {
        public Action? BeforeNext { get; set; }

        public bool Hooked { get; private set; }

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            if (BeforeNext is { } hook)
            {
                BeforeNext = null;
                Hooked = true;
                hook();
            }

            return inner.InvokeAsync(action, CancellationToken.None);
        }
    }

    public void Dispose()
    {
        workbench.Dispose();
        bench.Dispose();
    }
}
