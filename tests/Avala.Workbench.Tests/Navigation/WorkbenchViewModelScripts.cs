using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Conversation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Navigation;

public sealed class WorkbenchViewModelScripts : IDisposable
{
    private readonly Bench bench = new();
    private readonly WorkbenchViewModel workbench;
    private readonly List<IPage> requested = [];

    public WorkbenchViewModelScripts()
    {
        workbench = bench.Workbench();
        bench.Messenger.Register<PageRequested>(this, (_, message) => requested.Add(message.Page));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SelectingAJobOpensItsConversationAndAsksTheShellToShowThePage()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));

        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);

        var conversation = await bench.Ui.ReadAsync(() => workbench.Conversation);
        Assert.Equal((job.Job, "Fix JPY rounding in invoice totals"), (conversation?.Job, conversation?.Title));
        Assert.IsType<PromptViewModel>(Assert.Single(conversation!.Entries));
        Assert.Equal([workbench], requested);
    }

    [Fact]
    public async Task SelectingTheOpenJobAgainKeepsItsConversationAndItsReview()
    {
        var job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        bench.Publish(Bench.OnBoard(job));
        workbench.Activate();
        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);
        await bench.Ui.InvokeAsync(() => workbench.OpenReviewCommand.Execute(null), Cancellation);
        var (conversation, review) = await bench.Ui.ReadAsync(() => (workbench.Conversation, workbench.Review));

        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);

        Assert.Same(conversation, await bench.Ui.ReadAsync(() => workbench.Conversation));
        Assert.Same(review, await bench.Ui.ReadAsync(() => workbench.Review));
        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public async Task WhileActiveTheOpenConversationFollowsTheBoard()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        workbench.Activate();
        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);

        await ViewModelScript.Given(workbench).WhenPresentedAsync(
            _ => bench.Publish(Bench.OnBoard(job) with { Summary = job with { Status = JobStatus.NeedsHelp } }),
            Cancellation);

        Assert.Equal(JobStatus.NeedsHelp, await bench.Ui.ReadAsync(() => workbench.Conversation!.Status));
    }

    [Fact]
    public async Task AJobSelectedBeforeTheBoardKnowsItFillsWhenTheBoardBringsIt()
    {
        var job = bench.Job("Add invoice PDF endpoint", JobStatus.Preparing);
        workbench.Activate();
        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);
        var empty = await bench.Ui.ReadAsync(() => workbench.Conversation!.Entries.Count);

        await ViewModelScript.Given(workbench).WhenPresentedAsync(_ => bench.Publish(Bench.OnBoard(job)), Cancellation);

        Assert.Equal((0, 1), (empty, await bench.Ui.ReadAsync(() => workbench.Conversation!.Entries.Count)));
    }

    [Fact]
    public async Task AfterDeactivationTheBoardIsNoLongerFollowed()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        workbench.Activate();
        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);
        workbench.Deactivate();
        await workbench.Following;

        bench.Publish(Bench.OnBoard(job) with { Summary = job with { Status = JobStatus.NeedsHelp } });
        await bench.Ui.ReadAsync(() => true);

        Assert.Equal(JobStatus.Running, await bench.Ui.ReadAsync(() => workbench.Conversation!.Status));
    }

    [Fact]
    public async Task TheInspectorIsClosedUntilToggledAndThenInspectsTheOpenJob()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        workbench.Activate();
        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);
        var closed = (workbench.IsInspectorOpen, bench.Regions.ContextOf(ShellRegions.Inspector));

        await bench.Ui.InvokeAsync(() => workbench.ToggleInspectorCommand.Execute(null), Cancellation);
        var opened = (workbench.IsInspectorOpen, bench.Regions.ContextOf(ShellRegions.Inspector));
        await bench.Ui.InvokeAsync(() => workbench.ToggleInspectorCommand.Execute(null), Cancellation);

        Assert.Equal((false, Option<object>.None), closed);
        Assert.Equal((true, Option<object>.Some(job.Job)), opened);
        Assert.Equal(Option<object>.None, bench.Regions.ContextOf(ShellRegions.Inspector));
    }

    [Fact]
    public async Task TheOpenInspectorFollowsTheSelectionToAnotherJob()
    {
        var first = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        var second = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        workbench.Activate();
        await bench.Ui.InvokeAsync(
            () =>
            {
                Select(first);
                workbench.ToggleInspectorCommand.Execute(null);
                Select(second);
            },
            Cancellation);

        Assert.Equal(Option<object>.Some(second.Job), bench.Regions.ContextOf(ShellRegions.Inspector));
    }

    [Fact]
    public async Task TheInspectorWithoutASelectedJobInspectsNothing()
    {
        workbench.Activate();

        await bench.Ui.InvokeAsync(() => workbench.ToggleInspectorCommand.Execute(null), Cancellation);

        Assert.True(workbench.IsInspectorOpen);
        Assert.Empty(bench.Regions.Delivered);
    }

    [Fact]
    public async Task LeavingThePageClearsTheInspectorAndComingBackRestoresIt()
    {
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        workbench.Activate();
        await bench.Ui.InvokeAsync(
            () =>
            {
                Select(job);
                workbench.ToggleInspectorCommand.Execute(null);
            },
            Cancellation);

        workbench.Deactivate();
        var left = bench.Regions.ContextOf(ShellRegions.Inspector);
        workbench.Activate();

        Assert.Equal(Option<object>.None, left);
        Assert.Equal(Option<object>.Some(job.Job), bench.Regions.ContextOf(ShellRegions.Inspector));
    }

    [Fact]
    public async Task AReviewOpensOnlyForAJobAwaitingReviewOrHeldAndClosesWhenAnotherJobIsSelected()
    {
        var reviewed = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        var running = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(reviewed), Bench.OnBoard(running));
        workbench.Activate();

        await bench.Ui.InvokeAsync(() => Select(reviewed), Cancellation);
        await OpenReviewAsync();
        var opened = await bench.Ui.ReadAsync(() => workbench.Review is { IsLoaded: true } review ? review.Job : default);
        await bench.Ui.InvokeAsync(() => Select(running), Cancellation);

        Assert.Equal(
            (reviewed.Job, false, false),
            (opened, await bench.Ui.ReadAsync(() => workbench.Review is not null), await bench.Ui.ReadAsync(() => workbench.OpenReviewCommand.CanExecute(null))));
    }

    [Fact]
    public async Task ClosingTheReviewReturnsToTheConversation()
    {
        var job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        bench.Publish(Bench.OnBoard(job));
        workbench.Activate();
        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);
        await bench.Ui.InvokeAsync(() => workbench.OpenReviewCommand.Execute(null), Cancellation);

        await bench.Ui.InvokeAsync(() => workbench.CloseReviewCommand.Execute(null), Cancellation);

        Assert.Null(await bench.Ui.ReadAsync(() => workbench.Review));
        Assert.NotNull(await bench.Ui.ReadAsync(() => workbench.Conversation));
    }

    [Fact]
    public async Task AnOpenReviewReloadsWhenItsJobsRevisionMoves()
    {
        var job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        bench.Publish(Bench.OnBoard(job));
        workbench.Activate();
        await bench.Ui.InvokeAsync(() => Select(job), Cancellation);
        await OpenReviewAsync();
        var review = await bench.Ui.ReadAsync(() => workbench.Review!);
        var before = await bench.Ui.ReadAsync(() => review.Changes);
        bench.Changes.Files = [new Workspaces.Contracts.FileChange("src/auth/login.ts", Workspaces.Contracts.ChangeKind.Modified, 24, 3)];

        await ViewModelScript.Given(review).WhenPresentedAsync(_ => bench.Publish(Bench.OnBoard(job, revision: 1)), Cancellation);

        Assert.Equal(("No files changed", "1 file changed"), (before, await bench.Ui.ReadAsync(() => review.Changes)));
    }

    [Fact]
    public async Task AReviewLoadQueuedForTheUiBeforeThePageWasDeactivatedIsNeverShown()
    {
        var hooked = new HookedDispatcher(bench.Ui);
        using var page = bench.Workbench(hooked);
        var job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        bench.Publish(Bench.OnBoard(job));
        page.Activate();
        await bench.Ui.InvokeAsync(() => page.Receive(new JobSelected(job.Job)), Cancellation);
        hooked.BeforeNext = page.Deactivate;

        await bench.Ui.InvokeAsync(() => page.OpenReviewCommand.Execute(null), Cancellation);
        await (await bench.Ui.ReadAsync(() => page.Loading));

        Assert.Equal((true, false), (hooked.Hooked, await bench.Ui.ReadAsync(() => page.Review!.IsLoaded)));
    }

    public void Dispose()
    {
        workbench.Dispose();
        bench.Dispose();
    }

    private void Select(JobSummary job) => bench.Messenger.Send(new JobSelected(job.Job));

    private async Task OpenReviewAsync()
    {
        await bench.Ui.InvokeAsync(() => workbench.OpenReviewCommand.Execute(null), Cancellation);
        await (await bench.Ui.ReadAsync(() => workbench.Loading));
    }
}
