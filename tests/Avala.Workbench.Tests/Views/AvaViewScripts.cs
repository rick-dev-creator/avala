using Avala.Agents.Contracts.Sessions;
using Avala.Components.Status;
using Avala.Components.UI.Theme;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class AvaViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static AvaViewModel Moved(StatusKind status)
    {
        var ava = new AvaViewModel();
        ava.Show(StatusKind.Working);
        ava.Show(status);

        return ava;
    }

    private static ViewScript Shown(IAvaViewModel ava, double width) =>
        Screen.Show(new DesignConversationViewModel { Ava = ava }, ThemeVariant.Dark, width, 900);

    private static Rect Box(ViewScript view, string name)
    {
        var control = view.Find(name);

        return new Rect(control.TranslatePoint(default, view.Window) ?? default, control.Bounds.Size);
    }

    private static Border Bar(ViewScript view) =>
        view.Find("AvaBar").GetVisualDescendants().OfType<Border>().First(border => border.Name == "Bar");

    [Fact]
    public Task InAWideWindowAvaStandsInTheMarginBesideTheConversationAsync() =>
        ui.RunAsync(() =>
        {
            var view = Shown(AvaViewModel.Of(StatusKind.Working), 1440);
            var ava = Box(view, "AvaStanding");
            var composer = Box(view, "Composer");

            Assert.Equal((true, false), (view.Shows("AvaStanding"), view.Shows("AvaBar")));
            Assert.True(ava.Right <= composer.Left - 12, $"Ava ends at {ava.Right:0}, the composer starts at {composer.Left:0}");
            Assert.True(ava.Width >= 180, $"Ava is {ava.Width:0} wide");
        }, Cancellation);

    [Fact]
    public Task WhenTheReadingColumnWidensWithTheWindowAvaStaysClearOfItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Shown(AvaViewModel.Of(StatusKind.Working), 1340);
            Reading.SetFollows(view.Window, true);
            view.Settle();
            var ava = Box(view, "AvaStanding");
            var composer = Box(view, "Composer");

            Assert.True(composer.Width > 760, $"the column is {composer.Width:0} wide");
            Assert.True(view.Shows("AvaStanding"));
            Assert.True(ava.Right <= composer.Left - 12, $"Ava ends at {ava.Right:0}, the composer starts at {composer.Left:0}");
        }, Cancellation);

    [Fact]
    public Task InANarrowWindowAvaIsABarAboveThePlanAndTheComposerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Shown(AvaViewModel.Of(StatusKind.Working), 900);
            var bar = Box(view, "AvaBar");
            var plan = Box(view, "PlanPanel");

            Assert.Equal((false, true), (view.Shows("AvaStanding"), view.Shows("AvaBar")));
            Assert.True(bar.Bottom <= plan.Top + 0.5, $"the bar ends at {bar.Bottom:0}, the plan starts at {plan.Top:0}");
            Assert.InRange(bar.Height, 40, 70);
        }, Cancellation);

    [Fact]
    public Task WhenTheConversationNarrowsAvaFoldsIntoTheBarAndStandsAgainWhenItWidensAsync() =>
        ui.RunAsync(() =>
        {
            var view = Shown(AvaViewModel.Of(StatusKind.Working), 1440);

            view.Window.Width = 900;
            view.Settle();
            var narrowed = (view.Shows("AvaStanding"), view.Shows("AvaBar"));
            view.Window.Width = 1440;
            view.Settle();

            Assert.Equal((false, true), narrowed);
            Assert.Equal((true, false), (view.Shows("AvaStanding"), view.Shows("AvaBar")));
        }, Cancellation);

    [Fact]
    public Task AMomentWorthYourAttentionOpensTheBarAndFoldsItAfterSixSecondsAsync() =>
        ui.RunAsync(() =>
        {
            var ava = Moved(StatusKind.Failed);
            var view = Shown(ava, 900);
            view.Elapse(TimeSpan.FromSeconds(1));
            var opened = (ava.IsOpen, Bar(view).Bounds.Height);

            view.Elapse(TimeSpan.FromSeconds(4));
            var lingering = ava.IsOpen;
            view.Elapse(TimeSpan.FromSeconds(2));
            view.Elapse(TimeSpan.FromSeconds(1));

            Assert.True(opened.IsOpen);
            Assert.True(opened.Height > 100, $"the open bar is {opened.Height:0} high");
            Assert.True(lingering);
            Assert.False(ava.IsOpen);
            Assert.InRange(Bar(view).Bounds.Height, 40, 70);
        }, Cancellation);

    [Fact]
    public Task WhileTheJobNeedsYouTheBarStaysOpenAsync() =>
        ui.RunAsync(() =>
        {
            var ava = Moved(StatusKind.NeedsYou);
            var view = Shown(ava, 900);

            view.Elapse(TimeSpan.FromSeconds(10));
            view.Type("Composer", "x");

            Assert.True(ava.IsOpen);
        }, Cancellation);

    [Fact]
    public Task TypingFoldsAnOpenMomentAsync() =>
        ui.RunAsync(() =>
        {
            var ava = Moved(StatusKind.ReadyForReview);
            var view = Shown(ava, 900);

            view.Type("Composer", "x");

            Assert.False(ava.IsOpen);
        }, Cancellation);

    [Fact]
    public Task StandingAvaStaysInSightWhileHerMomentPassesSoTheBarNeverReopensItAsync() =>
        ui.RunAsync(() =>
        {
            var ava = Moved(StatusKind.Failed);
            var view = Shown(ava, 1440);

            view.Elapse(TimeSpan.FromSeconds(8));
            var standing = view.Shows("AvaStanding");
            view.Window.Width = 900;
            view.Settle();
            view.Elapse(TimeSpan.FromSeconds(1));

            Assert.True(standing);
            Assert.False(ava.IsOpen);
            Assert.InRange(Bar(view).Bounds.Height, 40, 70);
            Assert.False(view.Find("AvaStanding").GetVisualDescendants().OfType<StackPanel>().First(panel => panel.Name == "Standing").IsHitTestVisible);
        }, Cancellation);

    [Fact]
    public Task TheToggleSaysWhetherItFoldsOrOpensAvaAsync() =>
        ui.RunAsync(() =>
        {
            var ava = Moved(StatusKind.NeedsYou);
            var view = Shown(ava, 900);
            var toggle = view.Find("AvaBar").GetVisualDescendants().OfType<Button>().First(button => button.Name == "Toggle");
            var open = Avalonia.Automation.AutomationProperties.GetName(toggle);

            view.Click(toggle);

            Assert.Equal(("Fold Ava", "Open Ava"), (open, Avalonia.Automation.AutomationProperties.GetName(toggle)));
        }, Cancellation);

    [Fact]
    public Task AvaArrivesWithARiseWhenSheChangesFormAsync() =>
        ui.RunAsync(() =>
        {
            var view = Shown(AvaViewModel.Of(StatusKind.Working), 1440);

            Assert.Equal((MotionEntrance.Rise, MotionEntrance.None), (view.EntranceOf("AvaStanding"), view.EntranceOf("AvaBar")));
        }, Cancellation);

    [Fact]
    public Task AJobReachingReviewWhileYouReadTheLatestKeepsAvaOpenAsync() =>
        ui.RunAsync(() =>
        {
            var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", JobStatus.Running).Summary;
            var conversation = new Conversations(Composing.Of(new JobSteering(new FakeJobs(), new QueuedMessages(new FakeJobs(), Avala.Workbench.Tests.Composing.Drafts(), new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger())), new SendKeys()), new TimelineItems(Replying.With(new FakePermissionAnswers()), new FakeClipboard(), Tools.Viewing()), new TranscriptExport(new FakeClipboard(), new FakeFileSaver(), new FakeAppearance())).Open(summary.Job);
            var transcript = Transcript.Empty.WithPrompts(
                summary.Instruction,
                [.. Enumerable.Range(1, 20).Select(number => new AttemptRecord(number, number == 1 ? AttemptOrigin.Initial : AttemptOrigin.Hint, AttemptOutcome.Passed, "Keep going with the next currency", Option<SessionId>.None))]);
            conversation.Show(new BoardJob(summary, transcript));
            var view = Screen.Show(conversation);
            view.Window.Width = 900;
            view.Settle();
            var scroller = view.Find<ScrollViewer>("Scroller");
            var atEdge = scroller.Offset.Y > 0 && Math.Abs(scroller.Extent.Height - scroller.Viewport.Height - scroller.Offset.Y) < 1;

            conversation.Show(new BoardJob(summary with { Status = JobStatus.AwaitingReview }, transcript));
            var openedBeforeLayout = conversation.Ava.IsOpen;
            view.Settle();

            Assert.True(atEdge);
            Assert.True(openedBeforeLayout);
            Assert.Equal((StatusKind.ReadyForReview, true), (conversation.Pill.Kind, conversation.Ava.IsOpen));
        }, Cancellation);

    [Fact]
    public Task AWindowGrowingTallerIsNotYouScrollingBackAsync() =>
        ui.RunAsync(() =>
        {
            var ava = new AvaViewModel();
            ava.Show(StatusKind.Working);
            ava.Show(StatusKind.Failed);
            var view = Screen.Show(new DesignConversationViewModel { Ava = ava }, ThemeVariant.Dark, 900, 700);
            var scroller = view.Find<ScrollViewer>("Scroller");
            scroller.Offset = new Avalonia.Vector(0, scroller.Extent.Height);
            view.Settle();
            var opened = ava.IsOpen;

            view.Window.Height = 900;
            view.Settle();

            Assert.True(opened);
            Assert.True(ava.IsOpen);
        }, Cancellation);

    [Fact]
    public Task ANewMomentWhileAnotherLingersGetsItsOwnSixSecondsAsync() =>
        ui.RunAsync(() =>
        {
            var ava = new AvaViewModel();
            ava.Show(StatusKind.Working);
            ava.Show(StatusKind.ReadyForReview);
            var view = Screen.Show(new DesignConversationViewModel { Ava = ava }, ThemeVariant.Dark, 900, 900);
            view.Elapse(TimeSpan.FromSeconds(5));

            ava.Show(StatusKind.Done);
            view.Elapse(TimeSpan.FromSeconds(2));

            Assert.Equal(("done", true), (ava.Moment, ava.IsOpen));
        }, Cancellation);

    private static (ConversationViewModel Conversation, JobSummary Summary, Transcript Transcript, ViewScript View) AtTheLiveEdge()
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", JobStatus.Running).Summary;
        var conversation = new Conversations(Composing.Of(new JobSteering(new FakeJobs(), new QueuedMessages(new FakeJobs(), Avala.Workbench.Tests.Composing.Drafts(), new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger())), new SendKeys()), new TimelineItems(Replying.With(new FakePermissionAnswers()), new FakeClipboard(), Tools.Viewing()), new TranscriptExport(new FakeClipboard(), new FakeFileSaver(), new FakeAppearance())).Open(summary.Job);
        var transcript = Transcript.Empty.WithPrompts(
            summary.Instruction,
            [.. Enumerable.Range(1, 20).Select(number => new AttemptRecord(number, number == 1 ? AttemptOrigin.Initial : AttemptOrigin.Hint, AttemptOutcome.Passed, "Keep going with the next currency", Option<SessionId>.None))]);
        conversation.Show(new BoardJob(summary, transcript));
        var view = Screen.Show(conversation);
        view.Window.Width = 900;
        view.Settle();

        return (conversation, summary, transcript, view);
    }

    private static Point Over(ViewScript view, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), view.Window) ?? default;

    [Fact]
    public Task ClickingIntoTheComposerAsTheTurnEndsKeepsAvaOpenAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript, view) = AtTheLiveEdge();

            view.Click(view.Find<TextBox>("Draft"));
            conversation.Show(new BoardJob(summary with { Status = JobStatus.AwaitingReview }, transcript));
            view.Settle();

            Assert.Equal((StatusKind.ReadyForReview, true), (conversation.Pill.Kind, conversation.Ava.IsOpen));
        }, Cancellation);

    [Fact]
    public Task WheelingOnAsTheTurnEndsKeepsAvaOpenAsync() =>
        ui.RunAsync(() =>
        {
            var (conversation, summary, transcript, view) = AtTheLiveEdge();

            view.Window.MouseWheel(Over(view, view.Find("Scroller")), new Vector(0, -1));
            conversation.Show(new BoardJob(summary with { Status = JobStatus.AwaitingReview }, transcript));
            view.Settle();

            Assert.Equal((StatusKind.ReadyForReview, true), (conversation.Pill.Kind, conversation.Ava.IsOpen));
        }, Cancellation);
}
