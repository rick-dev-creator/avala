using Avala.Autopilot.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Triggers;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class LoopsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheSectionCountsItsLoopsAndShowsOneRowForEachAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignLoopsViewModel());

            Assert.Equal(("Loops · 3", 3, false), (view.TextOf("LoopsTitle"), view.Find<ItemsControl>("Loops").ItemCount, view.Shows("NoLoops")));
            Assert.Equal(3, view.All<TextBlock>().Count(text => text.Name == "LoopStatus"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task PauseAndStopInvokeTheLoopCommandsWithTheirLoopAsync() =>
        ui.RunAsync(() =>
        {
            var loops = new Recording();
            var view = Wide(loops);

            view.Click("Pause").Click("Stop");

            Assert.Equal(["pause ~/code/ledger-api", "stop ~/code/ledger-api"], loops.Calls);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutLoopsTheSectionSaysWhatStartsOneAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignLoopsViewModel { Loops = [] });

            Assert.True(view.Shows("NoLoops"));
        }, TestContext.Current.CancellationToken);

    private static ViewScript Wide(object viewModel)
    {
        var view = Screen.Show(viewModel);
        view.Window.Width = 1200;
        view.Window.Height = 1000;

        return view.Settle();
    }

    private sealed class Recording : ILoopsViewModel
    {
        public Recording()
        {
            PauseCommand = new AsyncRelayCommand<ILoopItemViewModel>(loop => RecordAsync($"pause {loop!.Repository}"));
            ResumeCommand = new AsyncRelayCommand<ILoopItemViewModel>(loop => RecordAsync($"resume {loop!.Repository}"));
            StopCommand = new AsyncRelayCommand<ILoopItemViewModel>(loop => RecordAsync($"stop {loop!.Repository}"));
        }

        public List<string> Calls { get; } = [];

        public IReadOnlyList<ILoopItemViewModel> Loops { get; } = [new DesignLoopItemViewModel()];

        public string Error => string.Empty;

        public IAsyncRelayCommand<ILoopItemViewModel> PauseCommand { get; }

        public IAsyncRelayCommand<ILoopItemViewModel> ResumeCommand { get; }

        public IAsyncRelayCommand<ILoopItemViewModel> StopCommand { get; }

        public void Show(IReadOnlyList<LoopDigest> loops, TimeZoneInfo zone)
        {
        }

        private Task RecordAsync(string call)
        {
            Calls.Add(call);

            return Task.CompletedTask;
        }
    }
}
