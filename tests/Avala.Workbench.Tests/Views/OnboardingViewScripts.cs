using Avala.Agents.Contracts.Sessions;
using Avala.Components.Privacy;
using Avala.Components.UI.Theme;
using Avala.Sdk.Telemetry;
using Avala.Testing.UI;
using Avala.Workbench.Onboarding;
using Avala.Workbench.Tests.Onboarding;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class OnboardingViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ContinueSlidesFromTheWelcomeThroughEachStepWithItsDotAndStartClosesTheTourAsync() =>
        ui.RunAsync(async () =>
        {
            var touring = new Touring();
            var tour = touring.Tour();
            tour.Activate();
            await tour.Checking;
            var view = Screen.Show(tour, ThemeVariant.Dark, 1100, 820);
            var seen = new List<(string Title, int Dot, string Continue)>();

            for (var step = 0; step < 4; step++)
            {
                view.Settle();
                seen.Add((view.All<TextBlock>().First(text => text.Classes.Contains("hero")).Text ?? string.Empty, Dot(view), view.Find<Button>("Continue").Content as string ?? string.Empty));
                view.Click("Continue");
                await tour.Loading;
            }

            await tour.Finishing;
            view.Settle();

            Assert.Equal(
                [("Welcome to Avala", 0, "Continue"), ("Your agents", 1, "Continue"), ("Your code", 2, "Continue"), ("Your privacy", 3, "Start")],
                seen);
            Assert.False(view.Shows("Card"));
            Assert.True(touring.Onboarding.IsDone);
        }, Cancellation);

    [Fact]
    public Task EachStepSlidesInAndTheSheetRisesOverAScrimAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOnboardingViewModel(), ThemeVariant.Dark, 1100, 820);

            Assert.Equal((MotionEntrance.Gentle, MotionEntrance.Sheet, MotionEntrance.Fade), (view.EntranceOf("Welcome"), view.EntranceOf("Card"), view.EntranceOf("Scrim")));
        }, Cancellation);

    [Fact]
    public Task EscapeSkipsTheTourAsync() =>
        ui.RunAsync(async () =>
        {
            var touring = new Touring();
            var tour = touring.Tour();
            tour.Activate();
            await tour.Checking;
            var view = Screen.Show(tour, ThemeVariant.Dark, 1100, 820);
            view.Settle();

            view.Press(Key.Escape);
            await tour.Finishing;

            Assert.Equal((false, true), (tour.IsShown, touring.Onboarding.IsDone));
        }, Cancellation);

    [Fact]
    public Task SkipIsOfferedOnEveryStepButTheLastAsync() =>
        ui.RunAsync(() =>
        {
            var first = Screen.Show(new DesignOnboardingViewModel(), ThemeVariant.Dark, 1100, 820);
            var last = Screen.Show(new DesignOnboardingViewModel { Step = new DesignPrivacyStepViewModel(), IsLast = true, ContinueLabel = OnboardingPhrases.Start }, ThemeVariant.Dark, 1100, 820);

            Assert.Equal((true, false), (first.Shows("Skip"), last.Shows("Skip")));
        }, Cancellation);

    private static int Dot(ViewScript view) =>
        view.All<Border>().Where(dot => dot.Classes.Contains("step")).Select((dot, at) => (dot, at)).Single(found => found.dot.Classes.Contains("current")).at;
}

public sealed class WelcomeStepViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheWelcomeShowsTheLogoAndWhatAvalaIsInOneLineAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWelcomeStepViewModel());

            Assert.Equal((true, "Welcome to Avala", "Agents you can trust without watching."), (view.Shows("AppIcon"), view.TextOf("Title"), view.TextOf("Text")));
        }, TestContext.Current.CancellationToken);
}

public sealed class AgentsStepViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task FoundAgentsShowAsRowsAndHideTheInstallCommandAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAgentsStepViewModel());

            Assert.Superset(new HashSet<string>(["claude-work", "claude-personal", "Signed in", "At its limit"]), view.VisibleTexts.ToHashSet());
            Assert.Equal((true, false), (view.Shows("Found"), view.Shows("Missing")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAnAgentTheInstallCommandsShowAndCheckAgainLooksAgainAsync() =>
        ui.RunAsync(async () =>
        {
            var touring = new Touring();
            var signedIn = touring.Connections.Catalog;
            touring.Connections.Catalog = signedIn with { Connections = [], Providers = [new ProviderInfo("harness", "Harness") { Install = "brew install harness" }] };
            touring.Connections.Discovered = signedIn;
            var agents = touring.Agents();
            await agents.LoadAsync(TestContext.Current.CancellationToken);
            var view = Screen.Show(agents);
            var said = (view.TextOf("NoAgents"), view.Find<SelectableTextBlock>("Command").Text);

            view.Click("CheckAgain");
            await agents.CheckAgainCommand.ExecutionTask!;
            view.Settle();

            Assert.Equal((OnboardingPhrases.NoAgentsText, "brew install harness"), said);
            Assert.Equal((true, false), (view.Shows("Found"), view.Shows("Missing")));
        }, TestContext.Current.CancellationToken);
}

public sealed class InstallRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task CopyPutsTheCommandOnTheClipboardAsync() =>
        ui.RunAsync(async () =>
        {
            var clipboard = new FakeClipboard();
            var install = new InstallRowViewModel("Harness", "brew install harness", AgentPhrases.SignIn("harness"), clipboard);
            var view = Screen.Show(install);

            view.Click("Copy");
            await install.CopyCommand.ExecutionTask!;

            Assert.Equal(["brew install harness"], clipboard.Copied);
            Assert.Equal(("Harness", "Then run harness once to sign in."), (view.TextOf("Provider"), view.TextOf("SignIn")));
        }, TestContext.Current.CancellationToken);
}

public sealed class AgentRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task EachStateShowsItsOwnMarkAsync() =>
        ui.RunAsync(() =>
        {
            var marks = new[] { AgentState.Ready, AgentState.AtLimit, AgentState.SignedOut }
                .Select(state => Screen.Show(new DesignAgentRowViewModel { State = state }))
                .Select(view => (view.Shows("Ready"), view.Shows("AtLimit"), view.Shows("SignedOut")));

            Assert.Equal([(true, false, false), (false, true, false), (false, false, true)], marks);
        }, TestContext.Current.CancellationToken);
}

public sealed class CodeStepViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheProjectsFolderIsMaskedAndChangeAsksForAnotherAsync() =>
        ui.RunAsync(() =>
        {
            var changes = 0;
            var view = Screen.Show(new DesignCodeStepViewModel { ChangeFolderCommand = new AsyncRelayCommand(() => Task.FromResult(changes++)) });

            view.Click("ChangeFolder");

            Assert.Equal((Personal.Path("/home/sam/Projects").Masked, 1), (view.TextOf("Folder"), changes));
            Assert.False(view.Shows("NoAccounts"));
        }, TestContext.Current.CancellationToken);
}

public sealed class PrivacyStepViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheTwoSwitchesTurnHidingAndUsageStatisticsOffAsync() =>
        ui.RunAsync(async () =>
        {
            var touring = new Touring();
            var privacy = touring.Privacy();
            await privacy.LoadAsync(TestContext.Current.CancellationToken);
            var view = Screen.Show(privacy);

            view.Click("HidePersonalDetails");
            view.Click("ShareStatistics");
            await privacy.Statistics.ToggleCommand.ExecutionTask!;
            view.Settle();

            Assert.Equal((false, TelemetryStatus.Off), (Assert.Single(touring.Appearance.Changes).HidesPersonalDetails, touring.Telemetry.State.Status));
            Assert.Equal((false, false), (view.Find<ToggleSwitch>("HidePersonalDetails").IsChecked, view.Find<ToggleSwitch>("ShareStatistics").IsChecked));
        }, TestContext.Current.CancellationToken);
}
