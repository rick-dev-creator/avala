using Avala.Sdk;
using Avala.Sdk.Appearance;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Onboarding;

internal interface IOnboardingStep
{
    Task LoadAsync(CancellationToken cancellationToken);
}

internal interface IOnboardingViewModel
{
    bool IsShown { get; }

    IOnboardingStep Step { get; }

    IReadOnlyList<OnboardingDot> Dots { get; }

    bool IsLast { get; }

    string ContinueLabel { get; }

    ICodeStepViewModel Code { get; }

    IRelayCommand ContinueCommand { get; }

    IRelayCommand SkipCommand { get; }
}

internal sealed record OnboardingDot(bool IsCurrent);

internal sealed record TourRequested(int Step);

internal sealed record OnboardingSteps(IOnboardingStep Welcome, IAgentsStepViewModel Agents, ICodeStepViewModel Code, IPrivacyStepViewModel Privacy)
{
    public IReadOnlyList<IOnboardingStep> All => [Welcome, Agents, Code, Privacy];
}

[INotifyPropertyChanged]
internal sealed partial class OnboardingViewModel : IOnboardingViewModel, IActivatable, IRecipient<TourRequested>
{
    private readonly IOnboarding onboarding;
    private readonly OnboardingSteps steps;
    private bool checkedOnce;

    public OnboardingViewModel(IOnboarding onboarding, OnboardingSteps steps, IMessenger messenger)
    {
        this.onboarding = onboarding;
        this.steps = steps;
        messenger.Register(this);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(SkipCommand))]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step), nameof(Dots), nameof(IsLast), nameof(ContinueLabel))]
    public partial int Index { get; private set; }

    public IOnboardingStep Step => steps.All[Index];

    public IReadOnlyList<OnboardingDot> Dots => [.. steps.All.Select((_, at) => new OnboardingDot(at == Index))];

    public bool IsLast => Index == steps.All.Count - 1;

    public string ContinueLabel => IsLast ? OnboardingPhrases.Start : OnboardingPhrases.Continue;

    public ICodeStepViewModel Code => steps.Code;

    public Task Checking { get; private set; } = Task.CompletedTask;

    public Task Loading { get; private set; } = Task.CompletedTask;

    public Task Finishing { get; private set; } = Task.CompletedTask;

    public void Activate()
    {
        if (!checkedOnce)
        {
            checkedOnce = true;
            Checking = CheckAsync();
        }
    }

    public void Deactivate()
    {
    }

    public void Receive(TourRequested message) => Open(Math.Clamp(message.Step, 0, steps.All.Count - 1));

    private async Task CheckAsync()
    {
        if (!await onboarding.IsDoneAsync(CancellationToken.None))
        {
            Open(0);
            await Loading;
        }
    }

    private void Open(int step)
    {
        Index = step;
        IsShown = true;
        Loading = Step.LoadAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Continue()
    {
        if (IsLast)
        {
            Finish();

            return;
        }

        Index++;
        Loading = Step.LoadAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Skip() => Finish();

    private void Finish()
    {
        IsShown = false;
        Finishing = FinishAsync();
    }

    private async Task FinishAsync()
    {
        _ = await onboarding.FinishAsync(CancellationToken.None);
        await steps.Privacy.ConcludeAsync(CancellationToken.None);
    }
}

internal static class OnboardingPhrases
{
    public const string Continue = "Continue";

    public const string Start = "Start";

    public const string WelcomeTitle = "Hi, I'm Ava.";

    public const string WelcomeText = "Leave it with me.";

    public const string WelcomeDetail = "I give each agent its own worktree and check every change before you review it.";

    public const string AgentsTitle = "Your agents";

    public const string AgentsText = "The coding agents signed in on this computer.";

    public const string NoAgentsText = "None found yet. Install one, then check again.";

    public const string CodeTitle = "Your code";

    public const string CodeText = "Where new projects go, and where you clone from.";

    public const string NoAccounts = "No GitHub, Gitea or Forgejo sign-in found. You can connect one later in Settings.";

    public const string PrivacyTitle = "Your privacy";

    public const string PrivacyText = "Both can be changed any time in Settings.";

    public const string HideDetails = "Masks emails, logins, account names and home folders on screen.";

    public const string Statistics = "The version, the operating system and a random install ID, at most once a day. Never code, prompts, paths or accounts.";
}
