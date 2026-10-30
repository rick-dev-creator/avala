using Avala.Workbench.Onboarding;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Onboarding;

public sealed class OnboardingViewModelScripts
{
    private readonly Touring touring = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFreshDataFolderOpensTheTourAtTheWelcomeAsync()
    {
        var tour = touring.Tour();

        tour.Activate();
        await tour.Checking;

        Assert.Equal((true, true, OnboardingPhrases.Continue), (tour.IsShown, tour.Step is WelcomeStepViewModel, tour.ContinueLabel));
        Assert.Equal([true, false, false, false], tour.Dots.Select(dot => dot.IsCurrent));
    }

    [Fact]
    public async Task AFinishedTourStaysClosedAsync()
    {
        touring.Onboarding.IsDone = true;
        var tour = touring.Tour();

        tour.Activate();
        await tour.Checking;

        Assert.False(tour.IsShown);
    }

    [Fact]
    public async Task ContinueWalksTheAgentsTheCodeAndThePrivacyStepsAndStartFinishesTheTourAsync()
    {
        var tour = touring.Tour();
        tour.Activate();
        await tour.Checking;
        var walked = new List<(Type Step, string Label)>();

        for (var step = 0; step < 3; step++)
        {
            tour.ContinueCommand.Execute(null);
            await tour.Loading;
            walked.Add((tour.Step.GetType(), tour.ContinueLabel));
        }

        tour.ContinueCommand.Execute(null);
        await tour.Finishing;

        Assert.Equal(
            [(typeof(AgentsStepViewModel), OnboardingPhrases.Continue), (typeof(CodeStepViewModel), OnboardingPhrases.Continue), (typeof(PrivacyStepViewModel), OnboardingPhrases.Start)],
            walked);
        Assert.Equal((false, 1), (tour.IsShown, touring.Onboarding.Finishes));
    }

    [Fact]
    public async Task SkipFinishesTheTourFromAnyStepAndCountsTheTelemetryNoticeAsShownAsync()
    {
        var tour = touring.Tour();
        tour.Activate();
        await tour.Checking;
        tour.ContinueCommand.Execute(null);
        await tour.Loading;

        tour.SkipCommand.Execute(null);
        await tour.Finishing;

        Assert.Equal((false, true), (tour.IsShown, touring.Onboarding.IsDone));
        Assert.False(await touring.Telemetry.ClaimNoticeAsync(Cancellation));
    }

    [Fact]
    public async Task ATourRequestedFromSettingsOpensItAgainAtTheWelcomeAsync()
    {
        touring.Onboarding.IsDone = true;
        var tour = touring.Tour();
        tour.Activate();
        await tour.Checking;

        touring.Messenger.Send(new TourRequested(0));
        await tour.Loading;

        Assert.Equal((true, 0), (tour.IsShown, tour.Index));
    }
}

public sealed class WelcomeStepViewModelScripts
{
    [Fact]
    public void TheWelcomeSaysWhatAvalaIsInOneLine() =>
        Assert.Equal(("Hi, I'm Ava.", "Leave it with me."), (new WelcomeStepViewModel().Title, new WelcomeStepViewModel().Text));
}
