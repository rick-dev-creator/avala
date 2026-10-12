namespace Avala.Workbench.Onboarding;

internal interface IWelcomeStepViewModel : IOnboardingStep
{
    string Title { get; }

    string Text { get; }

    string Detail { get; }
}

internal sealed class WelcomeStepViewModel : IWelcomeStepViewModel
{
    public string Title => OnboardingPhrases.WelcomeTitle;

    public string Text => OnboardingPhrases.WelcomeText;

    public string Detail => OnboardingPhrases.WelcomeDetail;

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
