using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avalonia;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Overview;
using Avala.Workbench.Tests.Views;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Overview;

public sealed class PeekViewModelScripts
{
    [Fact]
    public void APeekShowsOnlyTheLatestRowsOfItsJobsConversation()
    {
        var summary = Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running);
        var attempts = Enumerable.Range(1, 9).Select(number => new AttemptRecord(number, number == 1 ? AttemptOrigin.Initial : AttemptOrigin.Hint, AttemptOutcome.Passed, $"Step {number}", Option<Avala.Agents.Contracts.Sessions.SessionId>.None)).ToList();
        var peek = new PeekViewModel(Focusing.Conversations(new FakeJobs()).Open(summary.Job));

        peek.Show(new BoardJob(summary, Transcript.Empty.WithPrompts(summary.Instruction, attempts)));

        Assert.Equal(PeekViewModel.Shown, peek.Latest.Count);
        Assert.Equal("Step 9", Assert.IsType<Avala.Workbench.Conversation.PromptViewModel>(peek.Latest[^1]).Text);
    }
}

public sealed class PeekViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task APeekShowsItsRowsAboveTheJobsComposerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPeekViewModel(), Avalonia.Styling.ThemeVariant.Dark, 420, 700);

            Assert.Equal(3, view.Find<Avalonia.Controls.ItemsControl>("Latest").ItemCount);
            Assert.True(view.Shows("Draft"));
            Assert.True(view.Find("Latest").TranslatePoint(default, view.Window)?.Y < view.Find("Draft").TranslatePoint(default, view.Window)?.Y);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnOpenChildShowsItsPeekOnlyWhilePeekingAsync() =>
        ui.RunAsync(() =>
        {
            var peeking = Screen.Show(new DesignDelegationNodeViewModel { IsOpen = true, Peek = new DesignPeekViewModel() }, Avalonia.Styling.ThemeVariant.Dark, 420, 900).Shows("Peek");
            var open = Screen.Show(new DesignDelegationNodeViewModel { IsOpen = true }, Avalonia.Styling.ThemeVariant.Dark, 420, 900).Shows("Peek");

            Assert.Equal((true, false), (peeking, open));
        }, TestContext.Current.CancellationToken);
}
