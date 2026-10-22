using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class MentionsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSuggestionsListEachMentionWithWhatItIsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignMentionsViewModel());

            Assert.Equal(4, view.Find<ItemsControl>("Suggestions").ItemCount);
            Assert.Equal(("src/Checkout/PriceRounding.cs", true, true), (view.TextOf("Label"), view.Shows("FileIcon"), view.HasClass("Row", "selected")));
        }, Cancellation);

    [Fact]
    public Task InTheDraftTheArrowsPickASuggestionAndEnterWritesItWithoutSendingAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.NeedsHelp);
            var view = Screen.Show(composer);

            view.Type("Draft", "Look at @Pri");
            await ((MentionsViewModel)composer.Mentions).Looking;
            view.Settle();
            var opened = view.Find<Popup>("MentionsPopup").IsOpen;
            view.Press(Key.Down);
            view.Press(Key.Enter);
            view.Settle();

            Assert.True(opened);
            Assert.Equal(("Look at @src/Checkout/PriceTags.cs ", false, 0), (view.Find<TextBox>("Draft").Text, view.Find<Popup>("MentionsPopup").IsOpen, jobs.Calls.Count));
        }, Cancellation);

    [Fact]
    public Task EscapeClosesTheSuggestionsWithoutInterruptingTheJobAsync() =>
        ui.RunAsync(async () =>
        {
            var (composer, jobs) = Composer(JobStatus.Running);
            var view = Screen.Show(composer);

            view.Type("Draft", "@Rou");
            await ((MentionsViewModel)composer.Mentions).Looking;
            view.Press(Key.Escape);
            view.Settle();

            Assert.Equal((false, 0), (composer.Mentions.IsOpen, jobs.Calls.Count));
        }, Cancellation);

    private static (ComposerViewModel Composer, FakeJobs Jobs) Composer(JobStatus status)
    {
        var jobs = new FakeJobs { Status = status, Steerable = false };
        var summary = new FakeCatalog().Add("Extract sync queue into a module", status).Summary;
        var mentions = Composing.Mentioning(new Worktree(), "src/Checkout/Rounding.cs", "src/Checkout/Prices.cs", "src/Checkout/PriceTags.cs");
        var composer = new ComposerViewModel(summary.Job, new JobSteering(jobs, new QueuedMessages(jobs, Avala.Workbench.Tests.Composing.Drafts(), new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger())), Composing.Tools(new SendKeys(), summary.Job) with { Mentions = mentions }, Composing.Harness(summary.Job));
        composer.Track(new BoardJob(summary, Transcript.Empty));

        return (composer, jobs);
    }
}

public sealed class MentionRowViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARowShowsTheMentionAndClickingItChoosesItAsync() =>
        ui.RunAsync(() =>
        {
            var chosen = 0;
            var view = Screen.Show(new DesignMentionRowViewModel { Label = "a3f9c21", Detail = "Round prices", Kind = Avala.Workbench.Mentioning.MentionKind.Commit, ChooseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => chosen++) });

            view.Click("Row");

            Assert.Equal(("a3f9c21", "Round prices", true, false, 1), (view.TextOf("Label"), view.TextOf("Detail"), view.Shows("CommitIcon"), view.HasClass("Row", "selected"), chosen));
        }, Cancellation);
}
