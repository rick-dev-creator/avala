using Avala.Agents.Contracts.Events;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Board;
using Avala.Workbench.Timeline;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace Avala.Workbench.Tests.Views;

public sealed class ConversationViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheConversationShowsItsHeaderEveryEntryInOrderAndTheComposerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConversationViewModel());

            Assert.Equal("Fix JPY rounding in invoice totals", view.TextOf("Title"));
            Assert.Equal("Plan 2 of 4", view.TextOf("Plan"));
            Assert.Equal(9, view.Find<ItemsControl>("Entries").ItemCount);
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "PermissionCardView");
            Assert.True(view.Shows("Send"));
        }, Cancellation);

    [Fact]
    public Task AnEntryArrivingWhileTheConversationIsShownAppearsWithoutReplacingTheOthersAsync() =>
        ui.RunAsync(() =>
        {
            var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", JobStatus.Running).Summary;
            var conversation = new Conversations(new JobSteering(new FakeJobs(), new JobBoard()), new(new FakePermissionAnswers(), new FakeAgents())).Open(summary.Job);
            var transcript = Transcript.Empty.WithPrompts(summary.Instruction, []);
            conversation.Show(new BoardJob(summary, transcript));
            var view = Screen.Show(conversation);
            var prompt = view.Find<ItemsControl>("Entries").ContainerFromIndex(0);

            conversation.Show(new BoardJob(summary, transcript.WithRestart()));
            view.Settle();

            Assert.Same(prompt, view.Find<ItemsControl>("Entries").ContainerFromIndex(0));
            Assert.Contains("Avala restarted. The agent's work before this point is summarized by its attempts above.", view.VisibleTexts);
        }, Cancellation);
}

public sealed class ComposerViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARunningJobOffersInterruptAndStopButNotSendAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignComposerViewModel());

            Assert.Equal((false, true, true), (view.Find<Button>("Send").IsEffectivelyEnabled, view.Find<Button>("Interrupt").IsEffectivelyEnabled, view.Find<Button>("Stop").IsEffectivelyEnabled));
            Assert.False(view.Shows("Error"));
        }, Cancellation);

    [Fact]
    public Task TypingAMessageAndPressingControlEnterSendsItAsync() =>
        ui.RunAsync(async () =>
        {
            var jobs = new FakeJobs();
            var board = new JobBoard();
            var summary = new FakeCatalog().Add("Extract sync queue into a module", JobStatus.NeedsHelp).Summary;
            board.Publish(System.Collections.Immutable.ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));
            var composer = new ComposerViewModel(summary.Job, new JobSteering(jobs, board));
            composer.Track(JobStatus.NeedsHelp);
            var view = Screen.Show(composer);

            view.Type("Draft", "Carry on with the queue");
            view.Press(Key.Enter, RawInputModifiers.Control);
            await (composer.SendCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal(["continue Carry on with the queue"], jobs.Calls);
            Assert.Equal(string.Empty, view.Find<TextBox>("Draft").Text);
        }, Cancellation);

    [Fact]
    public Task ARefusalShowsUnderTheComposerInTheFailureColorAsync() =>
        ui.RunAsync(async () =>
        {
            var jobs = new FakeJobs { Refusal = JobRejection.NotRunning };
            var board = new JobBoard();
            var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", JobStatus.Running).Summary;
            board.Publish(System.Collections.Immutable.ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));
            var composer = new ComposerViewModel(summary.Job, new JobSteering(jobs, board));
            composer.Track(JobStatus.Running);
            var view = Screen.Show(composer);

            view.Click("Stop");
            await (composer.StopCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal("The job is not running.", view.TextOf("Error"));
            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Error").Foreground).Color);
        }, Cancellation);
}

public sealed class PromptViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task APromptShowsWhoSentItItsOutcomeAndWhatWasSentAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPromptViewModel());

            Assert.Equal(("Instruction", "running"), (view.TextOf("Origin"), view.TextOf("Outcome")));
            Assert.True(view.HasClass("Bubble", "person"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARecoveryPromptShowsNoEmptyTextAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PromptViewModel(new PromptEntry("attempt:2", 2, AttemptOrigin.Recovery, Option<string>.None, Option<AttemptOutcome>.None)));

            Assert.False(view.Shows("Text"));
            Assert.False(view.HasClass("Bubble", "person"));
        }, TestContext.Current.CancellationToken);
}

public sealed class MessageViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AStreamingMessageSaysItIsStillWritingUntilItEndsAsync() =>
        ui.RunAsync(() =>
        {
            var message = new MessageViewModel(new MessageEntry("m", "Totals now round", Option<ItemOutcome>.None));
            var view = Screen.Show(message);
            var writing = view.Shows("Streaming");

            message.Update(new MessageEntry("m", "Totals now round to whole yen.", ItemOutcome.Succeeded));
            view.Settle();

            Assert.Equal((true, false), (writing, view.Shows("Streaming")));
            Assert.Equal("Totals now round to whole yen.", view.Find<SelectableTextBlock>("Text").Text);
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
    public Task ThinkingPulsesAndStopsWhenTheThoughtEndsAsync() =>
        ui.RunAsync(() =>
        {
            var reasoning = new ReasoningViewModel(new ReasoningEntry("r", string.Empty, DateTimeOffset.UnixEpoch, Option<TimeSpan>.None, Option<ItemOutcome>.None));
            var view = Screen.Show(reasoning);
            var thinking = view.HasClass("Thinking", "pulse");

            reasoning.Update(new ReasoningEntry("r", "Done", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(3), ItemOutcome.Succeeded));
            view.Settle();

            Assert.Equal((true, false), (thinking, view.HasClass("Thinking", "pulse")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ToolViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AToolIsOneLineThatOpensToItsOutputInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new ToolViewModel(new ToolEntry("t", ItemKind.Command, "npm test -- money.test.ts", "PASS src/money", ItemOutcome.Succeeded)));
            var collapsed = view.Shows("Details");

            view.Click("Toggle");

            Assert.Equal((false, true), (collapsed, view.Shows("Details")));
            Assert.Equal(("Command", "npm test -- money.test.ts", "done"), (view.TextOf("Kind"), view.TextOf("Title"), view.TextOf("Outcome")));
            Assert.Equal("JetBrains Mono", view.Find<TextBlock>("Title").FontFamily.FamilyNames[0]);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFailedToolSaysSoInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new ToolViewModel(new ToolEntry("t", ItemKind.Command, "npm test", "FAIL", ItemOutcome.Failed)));

            Assert.True(view.HasClass("Outcome", "failure"));
            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Outcome").Foreground).Color);
        }, TestContext.Current.CancellationToken);
}

public sealed class PlanViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task APlanListsEachStepWithItsStatusAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPlanViewModel());

            Assert.Equal("Plan · 2 of 4", view.TextOf("Progress"));
            Assert.Superset(new HashSet<string>(["Done", "InProgress", "Pending", "Add JPY and EUR rounding tests"]), view.VisibleTexts.ToHashSet());
        }, TestContext.Current.CancellationToken);
}

public sealed class CanvasViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ACanvasShowsItsSurfaceWithItsTitleKindAndDrawingAndPulsesWhileStreamingAsync() =>
        ui.RunAsync(() =>
        {
            var canvas = new CanvasViewModel(new CanvasEntry("c", "Rounding before and after", "text/vnd.mermaid", "flowchart LR", CanvasStatus.Streaming));
            var view = Screen.Show(canvas);
            var streaming = view.Shows("Streaming");

            canvas.Update(new CanvasEntry("c", "Rounding before and after", "text/vnd.mermaid", "flowchart LR\n  A --> B", CanvasStatus.Completed));
            view.Settle();

            Assert.Equal((true, false, false), (streaming, view.Shows("Streaming"), view.Shows("Status")));
            Assert.Equal(("Rounding before and after", "Mermaid"), (view.TextOf("Title"), view.TextOf("MediaLabel")));
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
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ATurnWithoutCostShowsNoCostAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new TurnEndViewModel(new TurnEndEntry("t", TurnOutcome.Interrupted, TimeSpan.FromSeconds(9), default, [])));

            Assert.Equal("Interrupted after 9s", view.TextOf("Summary"));
            Assert.False(view.Shows("Cost"));
        }, TestContext.Current.CancellationToken);
}

public sealed class RestartViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheRestartNoteIsShownAsAQuietCaptionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new RestartViewModel());

            Assert.True(view.HasClass("Note", "caption"));
            Assert.Contains("Avala restarted.", view.TextOf("Note"), StringComparison.Ordinal);
        }, TestContext.Current.CancellationToken);
}
