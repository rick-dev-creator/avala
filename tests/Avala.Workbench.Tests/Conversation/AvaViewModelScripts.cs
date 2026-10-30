using Avala.Components.Status;
using Avala.Testing;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Conversation;

public sealed class AvaViewModelScripts
{
    [Theory]
    [InlineData(StatusKind.Working, Mood.Focused, "working")]
    [InlineData(StatusKind.Checking, Mood.Focused, "checking")]
    [InlineData(StatusKind.NeedsYou, Mood.Asking, "needs you")]
    [InlineData(StatusKind.Held, Mood.Asking, "held")]
    [InlineData(StatusKind.Failed, Mood.Worried, "failed")]
    [InlineData(StatusKind.ReadyForReview, Mood.Pleased, "ready for review")]
    [InlineData(StatusKind.Done, Mood.Pleased, "done")]
    public void AvaSaysWhatTheJobIsDoingInItsMood(StatusKind status, Mood mood, string moment) =>
        ViewModelScript.Given(AvaViewModel.Of(status))
            .Then(ava => Assert.Equal((mood, moment, true, true), (ava.Mood, ava.Moment, ava.Line.Length > 0, ava.Kanji.Length > 0)));

    [Fact]
    public void EachMoodSpeaksInItsOwnWords() =>
        Assert.Equal(
            Enum.GetValues<StatusKind>().Length,
            Enum.GetValues<StatusKind>().Select(status => AvaViewModel.Of(status).Line).Distinct().Count());

    [Fact]
    public void WhileTheJobWorksAvaStaysFolded() =>
        ViewModelScript.Given(new AvaViewModel())
            .When(ava => ava.Show(StatusKind.Working))
            .When(ava => ava.Show(StatusKind.Checking))
            .Then(ava => Assert.Equal((false, false, false), (ava.IsOpen, ava.IsWaiting, ava.IsLingering)));

    [Fact]
    public void WhenTheJobNeedsYouAvaOpensAndStaysOpenUntilItMovesOn() =>
        ViewModelScript.Given(new AvaViewModel())
            .When(ava => ava.Show(StatusKind.Working))
            .When(ava => ava.Show(StatusKind.NeedsYou))
            .Then(ava => Assert.Equal((true, true, false), (ava.IsOpen, ava.IsWaiting, ava.IsLingering)))
            .When(ava => ava.FoldCommand.Execute(null))
            .Then(ava => Assert.True(ava.IsOpen))
            .When(ava => ava.Show(StatusKind.Working))
            .Then(ava => Assert.Equal((false, false), (ava.IsOpen, ava.IsWaiting)));

    [Theory]
    [InlineData(StatusKind.Failed)]
    [InlineData(StatusKind.ReadyForReview)]
    [InlineData(StatusKind.Done)]
    public void AMomentWorthYourAttentionOpensAvaUntilSheIsFolded(StatusKind status) =>
        ViewModelScript.Given(new AvaViewModel())
            .When(ava => ava.Show(StatusKind.Working))
            .When(ava => ava.Show(status))
            .Then(ava => Assert.Equal((true, false, true), (ava.IsOpen, ava.IsWaiting, ava.IsLingering)))
            .When(ava => ava.FoldCommand.Execute(null))
            .Then(ava => Assert.Equal((false, false), (ava.IsOpen, ava.IsLingering)));

    [Fact]
    public void TheSameStatusShownAgainDoesNotOpenAvaAgain() =>
        ViewModelScript.Given(new AvaViewModel())
            .When(ava => ava.Show(StatusKind.Working))
            .When(ava => ava.Show(StatusKind.ReadyForReview))
            .When(ava => ava.FoldCommand.Execute(null))
            .When(ava => ava.Show(StatusKind.ReadyForReview))
            .Then(ava => Assert.False(ava.IsOpen));

    [Fact]
    public void AJobOpenedWhenItIsAlreadyReadyStartsWithAvaFolded() =>
        ViewModelScript.Given(new AvaViewModel())
            .When(ava => ava.Show(StatusKind.ReadyForReview))
            .Then(ava => Assert.Equal((false, false, Mood.Pleased), (ava.IsOpen, ava.IsLingering, ava.Mood)));

    [Fact]
    public void AJobOpenedWhileItNeedsYouStartsWithAvaOpen() =>
        ViewModelScript.Given(new AvaViewModel())
            .When(ava => ava.Show(StatusKind.NeedsYou))
            .Then(ava => Assert.Equal((true, true), (ava.IsOpen, ava.IsWaiting)));

    [Fact]
    public void TheToggleFoldsAvaEvenWhileSheWaitsAndOpensHerAgain() =>
        ViewModelScript.Given(new AvaViewModel())
            .When(ava => ava.Show(StatusKind.NeedsYou))
            .When(ava => ava.ToggleCommand.Execute(null))
            .Then(ava => Assert.False(ava.IsOpen))
            .When(ava => ava.ToggleCommand.Execute(null))
            .Then(ava => Assert.Equal((true, false), (ava.IsOpen, ava.IsLingering)));
}
