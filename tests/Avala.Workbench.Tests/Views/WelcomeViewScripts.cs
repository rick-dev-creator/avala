using Avala.Components.Privacy;
using Avala.Testing.UI;
using Avala.Workbench.Harnesses;
using Avala.Workbench.Welcome;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class WelcomeViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheWelcomeShowsTheQuestionTheComposerTheThreeStartsAndTheAccountsWithLoginsMaskedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWelcomeViewModel(), Avalonia.Styling.ThemeVariant.Dark, 1000, 760);

            Assert.Equal(WelcomePhrases.Title, view.TextOf("Hero"));
            Assert.Equal((true, true, true, true, true), (view.Shows("WelcomeInstruction"), view.Shows("OpenFolder"), view.Shows("Clone"), view.Shows("NewRepository"), view.Shows("Connect")));
            Assert.Equal(["claude-work", "oc•••••"], view.All<TextBlock>().Where(text => text.Name == "ChipLabel").Select(text => text.Text));
        }, Cancellation);

    [Fact]
    public Task TheHarnessChipSitsInsideTheComposerLeftOfSendAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWelcomeViewModel(), Avalonia.Styling.ThemeVariant.Dark, 1000, 760);
            var composer = view.Find("Composer");
            var chip = view.Find("HarnessButton");
            var send = view.Find("WelcomeStart");
            var chipEnd = chip.TranslatePoint(new Point(chip.Bounds.Width, 0), view.Window);
            var sendStart = send.TranslatePoint(default, view.Window);

            Assert.Equal(("Auto · claude-work", true, true), (view.TextOf("HarnessLabel"), composer.IsVisualAncestorOf(chip), chipEnd?.X <= sendStart?.X));
        }, Cancellation);

    [Fact]
    public Task WithoutAHarnessTheComposerIsDisabledAndTheChipCarriesTheFixAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(
                new DesignWelcomeViewModel { CanCompose = false, Chips = [new DesignAccountChipViewModel { Label = Personal.Plain("Claude Code"), IsAttention = true, FixLabel = WelcomePhrases.CheckAgain }] },
                Avalonia.Styling.ThemeVariant.Dark,
                1000,
                760);

            Assert.False(view.Find<TextBox>("WelcomeInstruction").IsEffectivelyEnabled);
            Assert.Equal((true, true, WelcomePhrases.CheckAgain), (view.HasClass("Chip", "attention"), view.Shows("ChipFix"), view.Find<Button>("ChipFix").Content));
        }, Cancellation);

    [Fact]
    public Task EnterInTheComposerStartsTheJobAsync() =>
        ui.RunAsync(() =>
        {
            var started = 0;
            var welcome = new Started(() => started++);
            var view = Screen.Show(welcome, Avalonia.Styling.ThemeVariant.Dark, 1000, 760);

            view.Type("WelcomeInstruction", "Sketch a parser").Press(Key.Enter);

            Assert.Equal(1, started);
        }, Cancellation);

    private sealed class Started(Action start) : IWelcomeViewModel
    {
        public bool CanCompose => true;

        public string Instruction { get; set; } = string.Empty;

        public string Error => string.Empty;

        public IReadOnlyList<IAccountChipViewModel> Chips => [];

        public IHarnessChoiceViewModel Harness { get; } = new DesignHarnessChoiceViewModel();

        public IAsyncRelayCommand StartCommand { get; } = new AsyncRelayCommand(() =>
        {
            start();

            return Task.CompletedTask;
        });

        public IAsyncRelayCommand OpenFolderCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

        public IAsyncRelayCommand CloneCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

        public IAsyncRelayCommand NewRepositoryCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

        public IAsyncRelayCommand ConnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

        public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

public sealed class AccountChipViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChipShowsItsMarkLabelAndStatusDotAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAccountChipViewModel());

            Assert.Equal(("claude-work", "CW", true, false, false), (view.TextOf("ChipLabel"), view.TextOf("ChipMonogram"), view.Shows("ChipDot"), view.HasClass("ChipDot", "attention"), view.Shows("ChipFix")));
        }, TestContext.Current.CancellationToken);
}
