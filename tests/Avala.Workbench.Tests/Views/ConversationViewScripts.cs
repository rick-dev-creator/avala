using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Components.UI.Streaming;
using Avala.Jobs.Contracts;
using Avala.Sdk;
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
            var view = Screen.Show(new DesignConversationViewModel());

            Assert.Equal(
                ["PromptView", "ToolView", "ToolView", "ReasoningView", "PlanView", "MessageView", "CanvasView", "ToolView", "ReasoningView", "MessageView"],
                Rows(view).Select(row => row.GetType().Name));
            Assert.True(view.Shows("Send"));
            Assert.False(view.Shows("Quiet"));
        }, Cancellation);

    [Fact]
    public Task AnEntryArrivingWhileTheConversationIsShownAppearsWithoutReplacingTheOthersAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript) = Open(JobStatus.Running);
            var view = Screen.Show(conversation);
            var prompt = view.Find<ItemsControl>("Entries").ContainerFromIndex(0);

            conversation.Show(new BoardJob(summary, transcript.WithRestart()));
            view.Settle();

            Assert.Same(prompt, view.Find<ItemsControl>("Entries").ContainerFromIndex(0));
            Assert.Contains("Avala restarted. The agent's work before this point is summarized by its attempts above.", view.VisibleTexts);
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

            var text = view.Find<StreamingText>("Text");
            Assert.True(text.Bounds.Width <= 720);
            Assert.True(text.Bounds.Height > 5 * text.LineHeight);
        }, Cancellation);

    private static IReadOnlyList<UserControl> Rows(ViewScript view) =>
        [.. view.Find<ItemsControl>("Entries").GetRealizedContainers().Select(container => Assert.IsAssignableFrom<UserControl>(((ContentPresenter)container).Child))];

    private static Transcript Attempts(Transcript transcript, JobSummary summary, int count) =>
        transcript.WithPrompts(
            summary.Instruction,
            [.. Enumerable.Range(1, count).Select(number => new AttemptRecord(number, number == 1 ? AttemptOrigin.Initial : AttemptOrigin.Hint, AttemptOutcome.Passed, "Keep going with the next currency", Option<SessionId>.None))]);

    private static (ConversationViewModel Conversation, JobSummary Summary, Transcript Transcript) Open(JobStatus status)
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary;
        var conversation = new Conversations(new JobSteering(new FakeJobs(), new JobBoard()), new(new FakePermissionAnswers(), new FakeAgents())).Open(summary.Job);
        var transcript = Transcript.Empty.WithPrompts(summary.Instruction, []);
        conversation.Show(new BoardJob(summary, transcript));

        return (conversation, summary, transcript);
    }
}

public sealed class ComposerViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARunningJobOffersInterruptAndStopButNotSendAndSaysHowToStepInAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignComposerViewModel());

            Assert.Equal((false, true, true), (view.Find<Button>("Send").IsEffectivelyEnabled, view.Find<Button>("Interrupt").IsEffectivelyEnabled, view.Find<Button>("Stop").IsEffectivelyEnabled));
            Assert.Equal("The agent is working · interrupt it to step in", view.Find<TextBox>("Draft").PlaceholderText);
            Assert.False(view.Shows("Error"));
        }, Cancellation);

    [Fact]
    public Task AnEndedJobTakesNothingAndSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignComposerViewModel(JobStatus.Approved));

            Assert.Equal((false, false, false), (view.Find<Button>("Send").IsEffectivelyEnabled, view.Find<Button>("Interrupt").IsEffectivelyEnabled, view.Find<Button>("Stop").IsEffectivelyEnabled));
            Assert.Equal("This job has ended", view.Find<TextBox>("Draft").PlaceholderText);
            Assert.Equal(Color.Parse("#4E4E56"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<Button>("Stop").GetVisualDescendants().OfType<Path>().Single().Fill).Color);
        }, Cancellation);

    [Fact]
    public Task TypingAMessageAndPressingControlEnterSendsItAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.NeedsHelp);
            var view = Screen.Show(composer);

            view.Type("Draft", "Carry on with the queue");
            view.Press(Key.Enter, RawInputModifiers.Control);
            await (composer.SendCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal(["continue Carry on with the queue"], jobs.Calls);
            Assert.Equal(string.Empty, view.Find<TextBox>("Draft").Text);
        }, Cancellation);

    [Fact]
    public Task TheKeyboardReachesInterruptAndStopFromTheDraftAsync() =>
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

            view.Click("Stop");
            await (composer.StopCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal("The job is not running.", view.TextOf("Error"));
            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Error").Foreground).Color);
        }, Cancellation);

    private static (ComposerViewModel Composer, FakeJobs Jobs) Composer(JobStatus status)
    {
        var jobs = new FakeJobs();
        var board = new JobBoard();
        var summary = new FakeCatalog().Add("Extract sync queue into a module", status).Summary;
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));
        var composer = new ComposerViewModel(summary.Job, new JobSteering(jobs, board));
        composer.Track(status);

        return (composer, jobs);
    }
}

public sealed class PromptViewScripts(HeadlessUi ui)
{
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
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Hint, "Use the ISO table", AttemptOutcome.Passed)));

            Assert.Equal(("You", "passed", true), (view.TextOf("Origin"), view.TextOf("Outcome"), view.Shows("Heading")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARecoveryPromptShowsNoEmptyTextAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Recovery, Option<string>.None, Option<AttemptOutcome>.None)));

            Assert.False(view.Shows("Text"));
            Assert.False(view.HasClass("Bubble", "person"));
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

public sealed class MessageViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AStreamingMessageGrowsGentlyWithACaretUntilItEndsAsync() =>
        ui.RunAsync(() =>
        {
            var message = new MessageViewModel(new MessageEntry("m", "Totals now round", Option<ItemOutcome>.None));
            var view = Screen.Show(message);
            var text = view.Find<StreamingText>("Text");
            var caret = text.ShowsCaret;

            message.Update(new MessageEntry("m", "Totals now round to whole yen", Option<ItemOutcome>.None));
            view.Settle();
            var arriving = text.Arriving;
            message.Update(new MessageEntry("m", "Totals now round to whole yen.", ItemOutcome.Succeeded));
            view.Settle();

            Assert.Equal((true, 1), (caret, arriving));
            Assert.Equal((false, 0), (text.ShowsCaret, text.Arriving));
            Assert.Equal("Totals now round to whole yen.", text.Stream);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARewrittenMessageShowsItsNewTextWholeAsync() =>
        ui.RunAsync(() =>
        {
            var message = new MessageViewModel(new MessageEntry("m", "Totals round", Option<ItemOutcome>.None));
            var view = Screen.Show(message);

            message.Update(new MessageEntry("m", "Rounding moved to Money", Option<ItemOutcome>.None));
            view.Settle();

            var text = view.Find<StreamingText>("Text");
            Assert.Equal((0, "Rounding moved to Money"), (text.Arriving, text.Stream));
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
            var view = Screen.Show(new ToolViewModel(new ToolEntry("t", ItemKind.Command, "npm test -- money.test.ts", "PASS src/money", ItemOutcome.Succeeded)));
            var collapsed = view.Shows("Details");

            view.Click("Toggle");

            Assert.Equal((false, true), (collapsed, view.Shows("Details")));
            Assert.Equal(("npm test -- money.test.ts", "PASS src/money"), (view.TextOf("Title"), view.Find<SelectableTextBlock>("Output").Text));
            Assert.Same(view.Window.FindResource("IconCommand"), view.Find<Path>("Icon").Data);
            Assert.Equal("JetBrains Mono", view.Find<TextBlock>("Title").FontFamily.FamilyNames[0]);
            Assert.False(view.Shows("Outcome"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFailedToolSaysSoInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new ToolViewModel(new ToolEntry("t", ItemKind.Command, "npm test", "FAIL", ItemOutcome.Failed)));

            Assert.Equal("failed", view.TextOf("Outcome"));
            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Outcome").Foreground).Color);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARunningToolSpinsInsteadOfItsIconAndKeepsItsOutputOpenAsItArrivesAsync() =>
        ui.RunAsync(() =>
        {
            var tool = new ToolViewModel(new ToolEntry("t", ItemKind.Command, "go test ./...", string.Empty, Option<ItemOutcome>.None));
            var view = Screen.Show(tool);
            var running = (view.Shows("Spinner"), view.HasClass("Spinner", "spin"), view.Shows("Icon"), view.Shows("NoOutput"));
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

public sealed class CanvasViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ACanvasShowsItsSurfaceInItsCardWithOneTitleKindAndDrawingAndSpinsWhileStreamingAsync() =>
        ui.RunAsync(() =>
        {
            var canvas = new CanvasViewModel(new CanvasEntry("c", "Rounding path", "text/vnd.mermaid", "flowchart LR", CanvasStatus.Streaming));
            var view = Screen.Show(canvas);
            var streaming = (view.Shows("Streaming"), view.HasClass("Streaming", "spin"));

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

public sealed class RestartViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheRestartIsAQuietMarkerAcrossTheConversationAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new RestartViewModel());

            Assert.True(view.HasClass("Note", "caption"));
            Assert.Contains("Avala restarted.", view.TextOf("Note"), StringComparison.Ordinal);
            Assert.Equal(2, view.All<Border>().Count(border => border.Classes.Contains("separator")));
        }, TestContext.Current.CancellationToken);
}
