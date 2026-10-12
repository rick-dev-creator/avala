using Avala.Testing;
using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Components.UI.Copying;
using Avala.Components.UI.Markdown;
using Avala.Components.UI.Theme;
using Avala.Delegation.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class ConversationViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheConversationShowsEveryEntryInOrderWithItsOwnViewAndTheComposerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConversationViewModel(), Avalonia.Styling.ThemeVariant.Dark, 640, 4000);

            Assert.Equal(
                ["PromptView", "ToolView", "ToolView", "ReasoningView", "PlanView", "MessageView", "CanvasView", "ToolView", "ReasoningView", "MessageView"],
                Rows(view).Select(row => row.GetType().Name));
            Assert.True(view.Shows("Interrupt"));
            Assert.False(view.Shows("Quiet"));
        }, Cancellation);

    [Fact]
    public Task AnEntryArrivingWhileTheConversationIsShownAppearsWithoutReplacingTheOthersAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript) = Open(JobStatus.Running);
            var view = Screen.Show(conversation);
            var prompt = view.Find<ItemsControl>("Entries").ContainerFromIndex(0);

            conversation.Show(new BoardJob(summary, transcript.WithRestart(kept: true)));
            view.Settle();

            Assert.Same(prompt, view.Find<ItemsControl>("Entries").ContainerFromIndex(0));
            Assert.Contains("Avala restarted. Everything above happened before the restart.", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task AtTheLiveEdgeTheConversationFollowsWhatArrivesAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript) = Open(JobStatus.Running);
            var view = Screen.Show(conversation);

            conversation.Show(new BoardJob(summary, Attempts(transcript, summary, 20)));
            view.Settle();

            var scroller = view.Find<ScrollViewer>("Scroller");
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.Equal(scroller.Extent.Height - scroller.Viewport.Height, scroller.Offset.Y, 1);
        }, Cancellation);

    [Fact]
    public Task ALongConversationBuildsOnlyItsNewestScreenfulAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript) = Open(JobStatus.Running);
            conversation.Show(new BoardJob(summary, Attempts(transcript, summary, 40)));
            var view = Screen.Show(conversation);
            var entries = view.Find<ItemsControl>("Entries");

            Assert.InRange(Rows(view).Count, 1, conversation.Entries.Count / 2);
            Assert.Null(entries.ContainerFromIndex(0));
            Assert.NotNull(entries.ContainerFromIndex(conversation.Entries.Count - 1));
        }, Cancellation);

    [Fact]
    public Task WhileTheReaderScrolledUpArrivingEntriesDoNotMoveTheConversationAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript) = Open(JobStatus.Running);
            var view = Screen.Show(conversation);
            var longer = Attempts(transcript, summary, 20);
            conversation.Show(new BoardJob(summary, longer));
            view.Settle();
            var scroller = view.Find<ScrollViewer>("Scroller");

            scroller.Offset = scroller.Offset.WithY(40);
            view.Settle();
            conversation.Show(new BoardJob(summary, Attempts(longer, summary, 26)));
            view.Settle();

            Assert.Equal(40, scroller.Offset.Y, 1);
        }, Cancellation);

    [Fact]
    public Task ScrolledUpARoundButtonJumpsBackToTheLatestAndADotSaysMoreArrivedAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript) = Open(JobStatus.Running);
            var view = Screen.Show(conversation);
            var longer = Attempts(transcript, summary, 20);
            conversation.Show(new BoardJob(summary, longer));
            view.Settle();
            var scroller = view.Find<ScrollViewer>("Scroller");
            var following = view.Shows("JumpToLatest");

            scroller.Offset = scroller.Offset.WithY(40);
            view.Settle();
            var away = (view.Shows("JumpToLatest"), view.Shows("NewBelow"));
            conversation.Show(new BoardJob(summary, Attempts(longer, summary, 26)));
            view.Settle();
            var arrived = view.Shows("NewBelow");
            view.Click("JumpToLatest");

            Assert.False(following);
            Assert.Equal((true, false), away);
            Assert.True(arrived);
            Assert.Equal(scroller.Extent.Height - scroller.Viewport.Height, scroller.Offset.Y, 1);
            Assert.False(view.Shows("JumpToLatest"));
        }, Cancellation);

    [Fact]
    public Task WhileTheAgentWorksItsRowFollowsTheLastEntryAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConversationViewModel());

            var entries = view.Find("Entries");
            var working = view.Find("Working");
            Assert.True(view.Shows("Verb"));
            Assert.Equal("Thinking", view.TextOf("Verb"));
            Assert.True(working.Bounds.Top >= entries.Bounds.Bottom);
            Assert.Matches("^[0-9]+s$", view.TextOf("Elapsed"));
        }, Cancellation);

    [Fact]
    public Task AConversationWithNothingSaidYetSaysSoQuietlyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(DesignConversationViewModel.Empty);

            Assert.True(view.Shows("Quiet"));
            Assert.Contains("Nothing said yet. The conversation appears here as the agent works.", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task LongMessagesWrapWithinTheReadingWidthAsync() =>
        ui.RunAsync(() =>
        {
            var words = string.Join(' ', Enumerable.Repeat("ToMinor multiplies every currency by one hundred before rounding", 30));
            var view = Screen.Show(new DesignConversationViewModel("Long", "billing-worker", new Components.Status.StatusPillViewModel(Components.Status.StatusKind.Working, "Running"), new DesignComposerViewModel(), [new DesignMessageViewModel(words, false)]));
            view.Window.Width = 1400;
            view.Settle();

            var text = view.Find<MarkdownText>("Text");
            Assert.True(text.Bounds.Width <= 720);
            Assert.True(text.Bounds.Height > 5 * 23);
        }, Cancellation);

    private static IReadOnlyList<UserControl> Rows(ViewScript view) =>
        [.. view.Find<ItemsControl>("Entries").GetRealizedContainers()
            .OrderBy(container => view.Find<ItemsControl>("Entries").IndexFromContainer(container))
            .Select(container => Assert.IsAssignableFrom<UserControl>(((ContentPresenter)container).Child))];

    private static Transcript Attempts(Transcript transcript, JobSummary summary, int count) =>
        transcript.WithPrompts(
            summary.Instruction,
            [.. Enumerable.Range(1, count).Select(number => new AttemptRecord(number, number == 1 ? AttemptOrigin.Initial : AttemptOrigin.Hint, AttemptOutcome.Passed, "Keep going with the next currency", Option<SessionId>.None))]);

    [Fact]
    public Task ThePlanSitsJustAboveTheComposerWithItsCurrentStepAndProgressAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConversationViewModel());
            var plan = view.Find("PlanBox");
            var composer = view.Find("Composer");

            Assert.True(view.Shows("PlanBox"));
            Assert.Equal(("Use each currency's exponent", "2 of 4"), (view.TextOf("PlanCurrent"), view.TextOf("PlanProgress")));
            Assert.Equal((true, false), (view.Shows("PlanCurrentInProgress"), view.Shows("PlanCurrentPending")));
            Assert.False(view.Shows("PlanSteps"));
            var bottom = Avalonia.VisualExtensions.TranslatePoint(plan, new Avalonia.Point(0, plan.Bounds.Height), view.Window);
            var top = Avalonia.VisualExtensions.TranslatePoint(composer, default, view.Window);
            Assert.True(bottom?.Y <= top?.Y, $"plan ends at {bottom}, composer starts at {top}");
        }, Cancellation);

    [Fact]
    public Task AJobWithoutAPlanShowsNoPlanAboveTheComposerAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, _, _) = Open(JobStatus.Running);
            var view = Screen.Show(conversation);

            Assert.False(view.Shows("PlanBox"));
            Assert.True(view.Shows("Interrupt"));
        }, Cancellation);

    private static (ConversationViewModel Conversation, JobSummary Summary, Transcript Transcript) Open(JobStatus status)
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary;
        var conversation = new Conversations(Composing.Of(new JobSteering(new FakeJobs(), new QueuedMessages(new FakeJobs())), new SendKeys()), new TimelineItems(Replying.With(new FakePermissionAnswers()), FakeLinks.Opening, new FakeClipboard(), Tools.Files()), new TranscriptExport(new FakeClipboard(), new FakeFileSaver(), new FakeAppearance())).Open(summary.Job);
        var transcript = Transcript.Empty.WithPrompts(summary.Instruction, []);
        conversation.Show(new BoardJob(summary, transcript));

        return (conversation, summary, transcript);
    }
}

public sealed class ComposerViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARunningJobOffersStopInPlaceOfSendUntilAMessageIsTypedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignComposerViewModel());
            var idle = (view.Shows("Send"), view.Shows("Interrupt"), view.Find<Button>("Interrupt").IsEffectivelyEnabled);

            view.Type("Draft", "Keep the alias");

            Assert.Equal((false, true, true), idle);
            Assert.Equal((true, false, true), (view.Shows("Send"), view.Shows("Interrupt"), view.Find<Button>("Send").IsEffectivelyEnabled));
            Assert.Equal("Message the agent while it works…", view.Find<TextBox>("Draft").PlaceholderText);
            Assert.Equal("Send into the running turn", ToolTip.GetTip(view.Find<Button>("Send")));
            Assert.False(view.Shows("Error"));
            Assert.False(view.Shows("QueuedNote"));
        }, Cancellation);

    [Fact]
    public Task AQueuedMessageShowsAboveTheComposerWithAWayToWithdrawItAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.Running);
            var view = Screen.Show(composer);

            view.Type("Draft", "Keep the old namespace as an alias");
            view.Press(Hotkeys.CommandEnter);
            await (composer.SendCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Empty(jobs.Calls);
            Assert.True(view.Shows("QueuedNote"));
            Assert.Equal("Keep the old namespace as an alias", view.TextOf("QueuedText"));
            Assert.Equal("Queue for when the agent stops", ToolTip.GetTip(view.Find<Button>("Send")));

            view.Click("Withdraw");
            view.Settle();

            Assert.False(view.Shows("QueuedNote"));
        }, Cancellation);

    [Fact]
    public Task AnEndedJobOffersNoButtonsAndSaysItEndedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignComposerViewModel(JobStatus.Approved));

            Assert.Equal((false, false), (view.Shows("Send"), view.Shows("Interrupt")));
            Assert.Equal("This job has ended", view.Find<TextBox>("Draft").PlaceholderText);
        }, Cancellation);

    [Theory]
    [InlineData(JobStatus.NeedsHelp)]
    [InlineData(JobStatus.AwaitingReview)]
    [InlineData(JobStatus.Checking)]
    [InlineData(JobStatus.Preparing)]
    public Task AJobThatIsNotRunningOffersSendAndNoStopAsync(JobStatus status) =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Composer(status).Composer);

            Assert.Equal((true, false), (view.Shows("Send"), view.Shows("Interrupt")));
        }, Cancellation);

    [Fact]
    public Task AnApprovedJobWhosePullRequestIsStillWatchedSaysSoInsteadOfEndedAsync() =>
        ui.RunAsync(() =>
        {
            var (composer, _) = Composer(JobStatus.Approved, OnPullRequest.WakeOnCi);
            var view = Screen.Show(composer);

            Assert.Equal((false, false), (view.Shows("Send"), view.Shows("Interrupt")));
            Assert.Equal("Approved · Avala watches pull request #7 and wakes the agent if CI fails or it conflicts", view.Find<TextBox>("Draft").PlaceholderText);
        }, Cancellation);

    [Fact]
    public Task EnterSendsTheMessageAndShiftEnterAddsALineAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.NeedsHelp);
            var view = Screen.Show(composer);

            view.Type("Draft", "Carry on");
            view.Press(Key.Enter, RawInputModifiers.Shift);
            var kept = (jobs.Calls.Count, view.Find<TextBox>("Draft").Text);
            view.Type("Draft", "with the queue");
            view.Press(Key.Enter);
            await (composer.EnterCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((0, "Carry on\n"), (kept.Count, kept.Text?.ReplaceLineEndings("\n")));
            Assert.Equal(["continue Carry on\nwith the queue"], jobs.Calls.Select(call => call.ReplaceLineEndings("\n")));
            Assert.Equal(string.Empty, view.Find<TextBox>("Draft").Text);
        }, Cancellation);

    [Fact]
    public Task WithCommandEnterChosenEnterAddsALineAndOnlyCommandEnterSendsAsync() =>
        ui.RunAsync(async () =>
        {
            var keys = new SendKeys();
            await keys.HandleAsync(new AppearanceChanged(AppearancePreference.Default with { Send = SendKey.CommandEnter }), Cancellation);
            var (composer, jobs) = Composer(JobStatus.NeedsHelp, keys: keys);
            var view = Screen.Show(composer);

            view.Type("Draft", "Carry on");
            view.Press(Key.Enter);
            var kept = (jobs.Calls.Count, view.Find<TextBox>("Draft").Text);
            view.Press(Hotkeys.CommandEnter);
            await (composer.SendCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal((0, "Carry on\n"), (kept.Count, kept.Text?.ReplaceLineEndings("\n")));
            Assert.Equal(["continue Carry on"], jobs.Calls);
        }, Cancellation);

    [Fact]
    public Task AnEditedPromptLandsInTheFocusedDraftWithTheCaretAtItsEndAsync() =>
        ui.RunAsync(() =>
        {
            var (composer, _) = Composer(JobStatus.NeedsHelp);
            var view = Screen.Show(composer);

            composer.Edit("Use the ISO table");
            view.Settle();

            var draft = view.Find<TextBox>("Draft");
            Assert.Equal(("Use the ISO table", true, 17), (draft.Text, draft.IsFocused, draft.CaretIndex));
        }, Cancellation);

    [Fact]
    public Task EscapeInTheDraftStopsTheRunningTurnAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.Running);
            var view = Screen.Show(composer);

            view.Find("Draft").Focus();
            view.Press(Key.Escape);
            await (composer.InterruptCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.StartsWith("hold", Assert.Single(jobs.Calls), StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public Task TypingAMessageAndPressingControlEnterSendsItAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.NeedsHelp);
            var view = Screen.Show(composer);

            view.Type("Draft", "Carry on with the queue");
            view.Press(Hotkeys.CommandEnter);
            await (composer.SendCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal(["continue Carry on with the queue"], jobs.Calls);
            Assert.Equal(string.Empty, view.Find<TextBox>("Draft").Text);
        }, Cancellation);

    [Fact]
    public Task TheKeyboardReachesStopFromTheDraftAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.Running);
            var view = Screen.Show(composer);
            view.Find("Draft").Focus();

            view.Press(Key.Tab);
            var first = view.Window.FocusManager?.GetFocusedElement();
            view.Press(Key.Space);
            await (composer.InterruptCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Same(view.Find("Interrupt"), first);
            Assert.StartsWith("hold", Assert.Single(jobs.Calls), StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public Task ARefusalShowsUnderTheComposerInTheFailureColorAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.Running);
            jobs.Refusal = JobRejection.NotRunning;
            var view = Screen.Show(composer);

            view.Click("Interrupt");
            await (composer.InterruptCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal("The job is not running.", view.TextOf("Error"));
            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Error").Foreground).Color);
        }, Cancellation);

    private static (ComposerViewModel Composer, FakeJobs Jobs) Composer(JobStatus status, OnPullRequest? watched = null, SendKeys? keys = null)
    {
        var jobs = new FakeJobs { Status = status, Steerable = false };
        var summary = new FakeCatalog().Add("Extract sync queue into a module", status).Summary;
        var shown = new BoardJob(summary, Transcript.Empty)
        {
            PullRequest = watched is { } policy
                ? new PullRequestWatchState(summary.Job, new ForgeName("github"), new PullRequestRef(7, new Uri("https://github.com/acme/shop/pull/7"), "avala/job", "main"), policy, 3)
                : Option<PullRequestWatchState>.None,
        };
        var composer = new ComposerViewModel(summary.Job, new JobSteering(jobs, new QueuedMessages(jobs)), keys ?? new SendKeys(), Composing.Harness(summary.Job));
        composer.Track(shown);

        return (composer, jobs);
    }
}

public sealed class PromptViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task UnderYourPromptCopyEditAndSendAgainAreOfferedAsTheJobAllowsAsync() =>
        ui.RunAsync(() =>
        {
            var prompt = new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Hint, "Use the ISO table", AttemptOutcome.Passed), new FakeClipboard());
            var view = Screen.ShowStill(prompt, 300);
            var before = (view.Shows("Copy"), view.Shows("Edit"), view.Shows("Retry"));

            prompt.CanEdit = true;
            prompt.CanResend = true;
            view.Settle();

            Assert.Equal((true, false, false), before);
            Assert.Equal((true, true, true), (view.Shows("Copy"), view.Shows("Edit"), view.Shows("Retry")));
            Assert.Equal(HorizontalAlignment.Right, view.Find("Actions").HorizontalAlignment);
        }, Cancellation);

    [Fact]
    public Task YourPromptsTextOffersCopyAndSelectAllOnRightClickAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Hint, "Use the ISO table", AttemptOutcome.Passed), new FakeClipboard()));
            var text = view.Find<SelectableTextBlock>("Text");
            var menu = Assert.IsType<MenuFlyout>(text.ContextFlyout);

            Assert.Equal(["Copy", "Select All"], menu.Items.OfType<MenuItem>().Select(item => item.Header as string));
        }, Cancellation);

    [Fact]
    public Task TheInstructionIsABubbleOnTheRightWithoutAnOriginAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPromptViewModel());

            Assert.True(view.HasClass("Bubble", "person"));
            Assert.Equal(HorizontalAlignment.Right, view.Find("Bubble").HorizontalAlignment);
            Assert.False(view.Shows("Heading"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFollowUpSaysWhoSentItAndHowItWentAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Hint, "Use the ISO table", AttemptOutcome.Passed), new FakeClipboard()));

            Assert.Equal(("You", "attempt 2 passed its checks", true), (view.TextOf("Origin"), view.TextOf("Outcome"), view.Shows("Heading")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARecoveryPromptShowsNoEmptyTextAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Recovery, Option<string>.None, Option<AttemptOutcome>.None), new FakeClipboard()));

            Assert.False(view.Shows("Text"));
            Assert.False(view.HasClass("Bubble", "person"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnAvalaPromptSuchAsAHandoffBriefIsDrawnAsMarkdownAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Handoff, "This job was handed off to you.\n\n## Checks\n- calculator: failed, exit 1", Option<AttemptOutcome>.None), new FakeClipboard()));
            var texts = view.All<TextBlock>().Select(text => text.Inlines is { Count: > 0 } inlines ? inlines.Text ?? string.Empty : text.Text ?? string.Empty).ToList();

            Assert.Contains("Checks", texts);
            Assert.DoesNotContain(texts, text => text.Contains("## ", StringComparison.Ordinal));
            Assert.Single(view.All<Control>(), control => control.Classes.Contains("markdown-list"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheHeadingsOfAnAvalaPromptStayAtTheSizeOfItsTextAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Handoff, "## Checks\n\nThe last verification failed.", Option<AttemptOutcome>.None), new FakeClipboard()));
            var heading = Assert.Single(view.All<TextBlock>(), text => text.Classes.Contains("markdown-h2"));
            var paragraph = Assert.Single(view.All<TextBlock>(), text => text.Classes.Contains("markdown-paragraph"));

            Assert.Equal((paragraph.FontSize, FontWeight.SemiBold), (heading.FontSize, heading.FontWeight));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALongInstructionWrapsInsideItsBubbleAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPromptViewModel(string.Join(' ', Enumerable.Repeat("TestInvoiceTotal_JPY has failed on main since the tax change.", 12))));

            Assert.True(view.Find("Bubble").Bounds.Width <= 560);
            Assert.True(view.Find<SelectableTextBlock>("Text").Bounds.Height > 46);
        }, TestContext.Current.CancellationToken);
}

public sealed class ReasoningViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheThoughtIsCollapsedUntilItsSummaryIsClickedAsync() =>
        ui.RunAsync(() =>
        {
            var reasoning = new ReasoningViewModel(new ReasoningEntry("r", "JPY has zero decimals", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(12), ItemOutcome.Succeeded));
            var view = Screen.Show(reasoning);
            var collapsed = view.Shows("Thought");

            view.Click("Toggle");

            Assert.Equal((false, true), (collapsed, view.Shows("Thought")));
            Assert.Equal("Thought for 12s", view.TextOf("Summary"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ThinkingMovesAndFallsStillWhenTheThoughtEndsAsync() =>
        ui.RunAsync(() =>
        {
            var reasoning = new ReasoningViewModel(new ReasoningEntry("r", string.Empty, DateTimeOffset.UnixEpoch, Option<TimeSpan>.None, Option<ItemOutcome>.None));
            var view = Screen.Show(reasoning);
            var thinking = (view.Shows("LiveDots"), view.Shows("Shimmer"), view.Shows("StillDots"), view.TextOf("Summary"));

            reasoning.Update(new ReasoningEntry("r", "Done", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(3), ItemOutcome.Succeeded));
            view.Settle();

            Assert.Equal((true, true, false, "Thinking"), thinking);
            Assert.Equal((false, false, true, "Thought for 3s"), (view.Shows("LiveDots"), view.Shows("Shimmer"), view.Shows("StillDots"), view.TextOf("Summary")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnUnsharedThoughtShowsNoChevronAndClickingItOpensNothingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new ReasoningViewModel(new ReasoningEntry("r", string.Empty, DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(12), ItemOutcome.Succeeded)));

            view.Click("Toggle");

            Assert.Equal(("Thought for 12s · content not shared by the harness", false, false), (view.TextOf("Summary"), view.Shows("Chevron"), view.Shows("Thought")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnOpenThoughtStaysOpenWhileItKeepsStreamingAsync() =>
        ui.RunAsync(() =>
        {
            var reasoning = new ReasoningViewModel(new ReasoningEntry("r", "ISO 4217", DateTimeOffset.UnixEpoch, Option<TimeSpan>.None, Option<ItemOutcome>.None));
            var view = Screen.Show(reasoning);
            view.Click("Toggle");

            reasoning.Update(new ReasoningEntry("r", "ISO 4217 gives JPY an exponent of 0", DateTimeOffset.UnixEpoch, Option<TimeSpan>.None, Option<ItemOutcome>.None));
            view.Settle();

            Assert.True(view.Shows("Thought"));
            Assert.Equal("ISO 4217 gives JPY an exponent of 0", view.Find<SelectableTextBlock>("Text").Text);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task EnterOnTheFocusedSummaryOpensTheThoughtAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new ReasoningViewModel(new ReasoningEntry("r", "JPY has zero decimals", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(2), ItemOutcome.Succeeded)));

            view.Find("Toggle").Focus();
            view.Press(Key.Enter);

            Assert.True(view.Shows("Thought"));
        }, TestContext.Current.CancellationToken);
}

public sealed class ToolViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AToolIsOneLineWithItsKindIconThatOpensToItsOutputInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Tools.Of(new ToolEntry("t", ItemKind.Command, "npm test -- money.test.ts", "PASS src/money", ItemOutcome.Succeeded)));
            var collapsed = view.Shows("Details");

            view.Click("Toggle");

            Assert.Equal((false, true), (collapsed, view.Shows("Details")));
            Assert.Equal(("npm test -- money.test.ts", "PASS src/money"), (view.TextOf("Title"), view.Find<SelectableTextBlock>("Output").Text));
            Assert.Same(view.Window.FindResource("IconCommand"), view.Find<Path>("Icon").Data);
            Assert.Equal("JetBrains Mono", view.Find<TextBlock>("Title").FontFamily.FamilyNames[0]);
            Assert.False(view.Shows("Outcome"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingAFileEditLineOpensItsHunksInAPopoverAsync() =>
        ui.RunAsync(async () =>
        {
            var changes = new FakeChanges { Hunks = { ["src/money.ts"] = Tools.Diff("src/money.ts") } };
            var tool = Tools.InWorktree(new ToolEntry("t", ItemKind.FileEdit, "Edit src/money.ts", string.Empty, ItemOutcome.Succeeded), changes);
            var view = Screen.Show(tool);

            view.Click("Toggle");
            await (tool.ToggleCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            var popover = view.Find<Avalonia.Controls.Primitives.Popup>("ChangesPopover");
            Assert.True(popover.IsOpen);
            Assert.False(view.Shows("Details"));
            Assert.Contains(popover.Child!.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "+    return units * pow10(exponent)");
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AWrittenPageOffersToOpenInTheBrowserWithoutAThumbnailAsync() =>
        ui.RunAsync(async () =>
        {
            var worktree = new Worktree();
            worktree.Files.Stored[System.IO.Path.Combine(Worktree.Folder, "sistema-solar.html")] = [60];
            var tool = worktree.Tool(new ToolEntry("t", ItemKind.FileEdit, "Write sistema-solar.html", string.Empty, ItemOutcome.Succeeded));
            var view = Screen.Show(tool);

            view.Click("OpenPreview");
            await (tool.Preview.OpenCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal((true, false), (view.Shows("OpenPreview"), view.Shows("Thumbnail")));
            Assert.Equal("Open in your browser", ToolTip.GetTip(view.Find<Button>("OpenPreview")));
            Assert.Equal([new Uri(System.IO.Path.Combine(Worktree.Folder, "sistema-solar.html"))], worktree.Opener.Opened);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task EveryKindOfToolShowsAnIconAsync() =>
        ui.RunAsync(() =>
        {
            var blank = Enum.GetValues<ItemKind>()
                .Where(kind => Screen.Show(Tools.Of(new ToolEntry("t", kind, "delegate", string.Empty, ItemOutcome.Succeeded))).Find<Path>("Icon").Data is null);

            Assert.Empty(blank);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFailedToolSaysSoInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Tools.Of(new ToolEntry("t", ItemKind.Command, "npm test", "FAIL", ItemOutcome.Failed)));

            Assert.Equal("failed", view.TextOf("Outcome"));
            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Outcome").Foreground).Color);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ADeniedToolSaysSoInTheQuietCaptionColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Tools.Of(new ToolEntry("t", ItemKind.FileEdit, "Edit REVIEW.md", string.Empty, ItemOutcome.Cancelled) { Denied = true }));

            Assert.Equal(("denied", true, false), (view.TextOf("Outcome"), view.Shows("Outcome"), view.HasClass("Outcome", "failure")));
            Assert.True(view.Window.TryFindResource("TextTertiaryBrush", view.Window.ActualThemeVariant, out var quiet) && ReferenceEquals(quiet, view.Find<TextBlock>("Outcome").Foreground));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ADelegationLineIsSetInTheInterfaceFontAndACommandInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var delegated = new DelegationRecord(SessionId.New(), new ItemId("delegate"), "Migrate the database", DateTimeOffset.UnixEpoch) { Child = JobId.New() };
            var delegation = Screen.Show(Tools.Of(new ToolEntry("t", ItemKind.Other, "delegate", string.Empty, Option<ItemOutcome>.None) { Delegation = delegated }));
            var waiting = Screen.Show(Tools.Of(new ToolEntry("w", ItemKind.Other, "wait_child", string.Empty, Option<ItemOutcome>.None) { WaitsFor = JobId.New() }));
            var command = Screen.Show(Tools.Of(new ToolEntry("c", ItemKind.Command, "dotnet test", string.Empty, Option<ItemOutcome>.None)));

            Assert.Equal(
                ("Inter", "Inter", "JetBrains Mono"),
                (delegation.Find<TextBlock>("Title").FontFamily.FamilyNames[0], waiting.Find<TextBlock>("Title").FontFamily.FamilyNames[0], command.Find<TextBlock>("Title").FontFamily.FamilyNames[0]));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARunningToolSpinsInsteadOfItsIconAndKeepsItsOutputOpenAsItArrivesAsync() =>
        ui.RunAsync(() =>
        {
            var tool = Tools.Of(new ToolEntry("t", ItemKind.Command, "go test ./...", string.Empty, Option<ItemOutcome>.None));
            var view = Screen.Show(tool);
            var running = (view.Shows("Spinner"), view.LoopOf("Spinner") == MotionLoop.Spin, view.Shows("Icon"), view.Shows("NoOutput"));
            view.Click("Toggle");

            tool.Update(new ToolEntry("t", ItemKind.Command, "go test ./...", "ok  internal/money", ItemOutcome.Succeeded));
            view.Settle();

            Assert.Equal((true, true, false, false), running);
            Assert.Equal((false, true, true), (view.Shows("Spinner"), view.Shows("Icon"), view.Shows("Details")));
            Assert.Equal("ok  internal/money", view.Find<SelectableTextBlock>("Output").Text);
        }, TestContext.Current.CancellationToken);
}

public sealed class PlanViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task APlanListsEachStepWithItsStateAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPlanViewModel());
            var steps = view.All<Grid>().Where(grid => grid.Classes.Contains("step")).ToList();

            Assert.Equal("2 of 4", view.TextOf("Progress"));
            Assert.Equal([true, true, false, false], steps.Select(step => step.Classes.Contains("done")));
            Assert.Equal([false, false, true, false], steps.Select(step => step.Classes.Contains("active")));
            Assert.Equal(TextDecorations.Strikethrough, steps[0].GetVisualDescendants().OfType<TextBlock>().Single().TextDecorations);
        }, TestContext.Current.CancellationToken);
}

public sealed class PlanPanelViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePanelIsOneLineUntilClickedThenListsEachStepWithItsStateAsync() =>
        ui.RunAsync(() =>
        {
            var panel = new PlanPanelViewModel();
            panel.Show(new PlanEntry("plan", [new PlanStep("Read the code", PlanStepStatus.Done), new PlanStep("Write the change", PlanStepStatus.InProgress), new PlanStep("Run the tests", PlanStepStatus.Pending)]));
            var view = Screen.Show(panel);
            var folded = view.Shows("PlanSteps");

            view.Click("PlanToggle");
            var steps = view.All<Grid>().Where(grid => grid.Classes.Contains("step")).ToList();

            Assert.Equal(("Write the change", "1 of 3", false), (view.TextOf("PlanCurrent"), view.TextOf("PlanProgress"), folded));
            Assert.True(view.Shows("PlanSteps"));
            Assert.Equal([true, false, false], steps.Select(step => step.Classes.Contains("done")));
            Assert.Equal([false, true, false], steps.Select(step => step.Classes.Contains("active")));
            Assert.Equal("Show the plan", Avalonia.Automation.AutomationProperties.GetName(view.Find("PlanToggle")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task APlanWithEveryStepDoneLeavesNoPanelAsync() =>
        ui.RunAsync(() =>
        {
            var panel = new PlanPanelViewModel();
            panel.Show(new PlanEntry("plan", [new PlanStep("Write the change", PlanStepStatus.Done)]));

            Assert.False(Screen.Show(panel).Shows("PlanBox"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheHeaderGlyphAndCaptionStandInTheColumnsOfTheStepGlyphsAndTitlesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPlanPanelViewModel { IsExpanded = true });
            var active = view.All<Grid>().Single(step => step.Classes.Contains("active"));

            Assert.Equal(
                (Center(view, active.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(dot => dot.IsEffectivelyVisible)), Left(view, active.GetVisualDescendants().OfType<TextBlock>().Single())),
                (Center(view, view.Find("PlanCurrentInProgress")), Left(view, view.Find("PlanCaption"))));
        }, TestContext.Current.CancellationToken);

    private static double Center(ViewScript view, Avalonia.Visual visual) =>
        Avalonia.VisualExtensions.TranslatePoint(visual, new Avalonia.Point(visual.Bounds.Width / 2, 0), view.Window)?.X ?? double.NaN;

    private static double Left(ViewScript view, Avalonia.Visual visual) =>
        Avalonia.VisualExtensions.TranslatePoint(visual, default, view.Window)?.X ?? double.NaN;
}

public sealed class CanvasViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ACanvasShowsItsSurfaceInItsCardWithOneTitleKindAndDrawingAndSpinsWhileStreamingAsync() =>
        ui.RunAsync(() =>
        {
            var canvas = new CanvasViewModel(new CanvasEntry("c", "Rounding path", "text/vnd.mermaid", "flowchart LR", CanvasStatus.Streaming));
            var view = Screen.Show(canvas);
            var streaming = (view.Shows("Streaming"), view.LoopOf("Streaming") == MotionLoop.Spin);

            canvas.Update(new CanvasEntry("c", "Rounding path", "text/vnd.mermaid", "flowchart LR\n  A --> B", CanvasStatus.Completed));
            view.Settle();

            Assert.Equal(((true, true), false, false), (streaming, view.Shows("Streaming"), view.Shows("Status")));
            Assert.Equal(("Rounding path", "Mermaid"), (view.TextOf("Title"), view.TextOf("MediaLabel")));
            Assert.Single(view.All<TextBlock>(), block => block.Name == "Title");
            Assert.Same(view.Find<Border>("Figure"), view.Find("Surface").GetVisualParent());
            Assert.Equal("flowchart LR\n  A --> B", string.Concat(view.All<SelectableTextBlock>().Last(block => block.Name == "CanvasSource").Inlines!.OfType<Run>().Select(run => run.Text)));
        }, TestContext.Current.CancellationToken);
}

public sealed class TurnEndViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheEndOfATurnShowsHowLongItTookItsTokensAndCostAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignTurnEndViewModel());

            Assert.Equal(("Worked for 2m 34s", "48,210 tokens", "0.4120 USD"), (view.TextOf("Summary"), view.TextOf("Tokens"), view.TextOf("Cost")));
            Assert.False(view.HasClass("Summary", "failure"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ATurnWithoutCostShowsNoCostAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new TurnEndViewModel(new TurnEndEntry("t", TurnOutcome.Interrupted, TimeSpan.FromSeconds(9), default, [])));

            Assert.Equal("Interrupted after 9s", view.TextOf("Summary"));
            Assert.False(view.Shows("Cost"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFailedTurnSaysSoInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new TurnEndViewModel(new TurnEndEntry("t", TurnOutcome.Failed, TimeSpan.FromSeconds(9), default, [])));

            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Summary").Foreground).Color);
        }, TestContext.Current.CancellationToken);
}

public sealed class InterjectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AMessageSentMidTurnIsAPersonBubbleOnTheRightWithItsNoteAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignInterjectionViewModel());

            Assert.Equal(HorizontalAlignment.Right, view.Find("Bubble").HorizontalAlignment);
            Assert.Equal("You, while it worked · joined the turn", view.TextOf("Note"));
            Assert.Contains("alias", view.Find<SelectableTextBlock>("Text").Text, StringComparison.Ordinal);
        }, TestContext.Current.CancellationToken);
}

public sealed class RestartViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheRestartIsAQuietMarkerAcrossTheConversationAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new RestartViewModel(new RestartEntry(EntryKeys.Restart, Kept: true)));

            Assert.True(view.HasClass("Note", "caption"));
            Assert.Contains("Avala restarted.", view.TextOf("Note"), StringComparison.Ordinal);
            Assert.Equal(2, view.All<Border>().Count(border => border.Classes.Contains("separator")));
        }, TestContext.Current.CancellationToken);
}
