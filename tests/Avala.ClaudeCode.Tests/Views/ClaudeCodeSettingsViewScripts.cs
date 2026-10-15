using Avala.ClaudeCode.Settings;

using Avala.Testing.UI;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

[assembly: AssemblyFixture(typeof(HeadlessUi))]

namespace Avala.ClaudeCode.Tests.Views;

internal static class Screen
{
    private static bool registered;

    public static ViewScript Show(object viewModel)
    {
        if (!registered)
        {
            new ClaudeCodePlugin().RegisterViews(HeadlessApp.Views);
            registered = true;
        }

        return ViewScript.Show(viewModel);
    }
}

public sealed class ClaudeCodeSettingsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSectionListsEachLoginUnderItsTitleAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignClaudeCodeSettingsViewModel());

            Assert.Equal(2, view.Find<ItemsControl>("Logins").ItemCount);
            Assert.Contains("Claude Code", view.All<TextBlock>().Select(text => text.Text));
        }, Cancellation);

    [Fact]
    public Task OneSwitchAboveTheLoginsStartsEveryJobOnThePhoneAsync() =>
        ui.RunAsync(() =>
        {
            var toggled = 0;
            var view = Screen.Show(new DesignClaudeCodeSettingsViewModel
            {
                ToggleStartsOnPhoneCommand = new AsyncRelayCommand(() =>
                {
                    toggled++;

                    return Task.CompletedTask;
                }),
            });

            var wasOn = view.Find<ToggleSwitch>("StartsOnPhone").IsChecked == true;
            view.Click("StartsOnPhone");

            Assert.True(wasOn);
            Assert.Equal(1, toggled);
            Assert.False(view.Shows("PhoneProblem"));
        }, Cancellation);

    [Fact]
    public Task ASwitchThatCouldNotBeSavedSaysSoAsync() =>
        ui.RunAsync(() => Assert.True(Screen.Show(new DesignClaudeCodeSettingsViewModel { HasProblem = true }).Shows("PhoneProblem")), Cancellation);

    [Fact]
    public Task WithoutLoginsTheSectionIsHiddenAsync() =>
        ui.RunAsync(() => Assert.False(Screen.Show(new DesignClaudeCodeSettingsViewModel { Logins = [] }).Shows("Logins")), Cancellation);
}

public sealed class ClaudeCodeLoginViewScripts(HeadlessUi ui)
{
    private static readonly string[] Segments = ["Small", "Standard", "Large", "Full"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ALoginShowsItsSelectedWindowAndWhatEachMessageReadsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignClaudeCodeLoginViewModel());

            Assert.Equal(("claude-work", "~/.claude-work"), (view.TextOf("LoginName"), view.TextOf("Where")));
            Assert.Equal([false, true, false, false], Segments.Select(segment => view.HasClass(segment, "selected")));
            Assert.Equal("Summarizes the conversation at about 217k tokens, so each message reads at most that much", view.TextOf("Summary"));
            Assert.Equal((true, true, false), (view.Find<ToggleSwitch>("AutoCompact").IsChecked == true, view.Find<ToggleSwitch>("IdleCompaction").IsChecked == true, view.Shows("Custom")));
        }, Cancellation);

    [Fact]
    public Task ASegmentChoosesItsWindowAndASwitchTogglesItsCompactionAsync() =>
        ui.RunAsync(() =>
        {
            var calls = new List<string>();
            var view = Screen.Show(new DesignClaudeCodeLoginViewModel
            {
                ChooseCommand = new AsyncRelayCommand<CompactChoice>(choice =>
                {
                    calls.Add(choice.ToString());

                    return Task.CompletedTask;
                }),
                ToggleIdleCompactionCommand = new AsyncRelayCommand(() =>
                {
                    calls.Add("idle");

                    return Task.CompletedTask;
                }),
            });

            view.Click("Large").Click("IdleCompaction");

            Assert.Equal(["Large", "idle"], calls);
        }, Cancellation);

    [Fact]
    public Task ARefusedSaveSaysWhyWithTryAgainAsync() =>
        ui.RunAsync(() =>
        {
            var retried = 0;
            var view = Screen.Show(new DesignClaudeCodeLoginViewModel
            {
                Problem = "Could not save this setting: Unwritable",
                RetryCommand = new AsyncRelayCommand(() =>
                {
                    retried++;

                    return Task.CompletedTask;
                }),
            });

            view.Click("Retry");

            Assert.Equal(("Could not save this setting: Unwritable", 1), (view.TextOf("Problem"), retried));
        }, Cancellation);
}
