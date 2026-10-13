using Avala.Testing.UI;
using Avala.Workbench.Usage;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class UsageLeftViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheChipShowsWhatIsLeftAndWarnsNearTheLimitAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageLeftViewModel());

            Assert.Equal(("12% left", true, true, true), (view.TextOf("Left"), view.Shows("Open"), view.HasClass("Left", "attention"), view.HasClass("Ring", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingTheChipOpensUsageAsync() =>
        ui.RunAsync(() =>
        {
            var chip = new Chip(isShown: true);
            var view = Screen.Show(chip);

            view.Click("Open");

            Assert.Equal(1, chip.Opened);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAWindowToShowTheChipIsHiddenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new Chip(isShown: false));

            Assert.False(view.Shows("Open"));
        }, TestContext.Current.CancellationToken);

    private sealed class Chip : IUsageLeftViewModel
    {
        public Chip(bool isShown)
        {
            IsShown = isShown;
            OpenCommand = new RelayCommand(() => Opened++);
        }

        public int Opened { get; private set; }

        public bool IsShown { get; }

        public string Connection => "claude-work";

        public string Left => "28% left";

        public double Used => 0.72;

        public bool IsNear => false;

        public string Summary => "Usage left on claude-work";

        public IReadOnlyList<ILimitViewModel> Windows => [];

        public IRelayCommand OpenCommand { get; }
    }
}
