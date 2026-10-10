using Avala.Testing.UI;
using Avala.Workbench.Triggers;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class TriggersViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageShowsEachTriggerTheWebhookEndpointWithItsTunnelNoteTheDeliveriesAndTheFilesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignTriggersViewModel());

            Assert.Equal(("Triggers", "Times in this computer's time zone, Europe/Madrid"), (view.TextOf("Title"), view.TextOf("Scope")));
            Assert.Equal((3, 2, 3), (view.Find<ItemsControl>("Triggers").ItemCount, view.Find<ItemsControl>("Deliveries").ItemCount, view.Find<ItemsControl>("Files").ItemCount));
            Assert.Contains("Tailscale Funnel", view.TextOf("Tunnel"), StringComparison.Ordinal);
            Assert.Equal(("nightly-deps", "Weekdays at 02:30", "Next · Mon 12 Oct 02:30"), (view.TextOf("TriggerName"), view.TextOf("Fires"), view.TextOf("Next")));
            Assert.Equal((false, false, true), (view.Shows("NoTriggers"), view.Shows("NoDeliveries"), view.Shows("Notice")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task RunNowAndDisableInvokeThePageCommandsWithTheirTriggerAndReloadReadsTheFilesAsync() =>
        ui.RunAsync(() =>
        {
            var page = new Recording();
            var view = Wide(page);

            view.Click("RunNow").Click("Toggle").Click("Reload");

            Assert.Equal(["run nightly-deps", "toggle nightly-deps", "reload"], page.Calls);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutTriggersThePageSaysHowToDeclareThemAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new Recording { Triggers = [], Deliveries = [] });

            Assert.Equal((true, true), (view.Shows("NoTriggers"), view.Shows("NoDeliveries")));
        }, TestContext.Current.CancellationToken);

    private static ViewScript Wide(object viewModel)
    {
        var view = Screen.Show(viewModel);
        view.Window.Width = 1200;
        view.Window.Height = 1400;

        return view.Settle();
    }

    private sealed class Recording : ITriggersViewModel
    {
        private readonly DesignTriggersViewModel design = new();

        public Recording()
        {
            RunNowCommand = new AsyncRelayCommand<ITriggerItemViewModel>(trigger => RecordAsync($"run {trigger!.Name}"));
            ToggleCommand = new AsyncRelayCommand<ITriggerItemViewModel>(trigger => RecordAsync($"toggle {trigger!.Name}"));
            ReloadCommand = new AsyncRelayCommand(() => RecordAsync("reload"));
        }

        public List<string> Calls { get; } = [];

        public string Title => design.Title;

        public string Scope => design.Scope;

        public string Endpoint => design.Endpoint;

        public string Tunnel => design.Tunnel;

        public string Notice => string.Empty;

        public string Error => string.Empty;

        public IReadOnlyList<ITriggerItemViewModel> Triggers { get; init; } = new DesignTriggersViewModel().Triggers;

        public IReadOnlyList<string> Files => design.Files;

        public IReadOnlyList<string> Deliveries { get; init; } = new DesignTriggersViewModel().Deliveries;

        public IAsyncRelayCommand<ITriggerItemViewModel> RunNowCommand { get; }

        public IAsyncRelayCommand<ITriggerItemViewModel> ToggleCommand { get; }

        public IAsyncRelayCommand ReloadCommand { get; }

        private Task RecordAsync(string call)
        {
            Calls.Add(call);

            return Task.CompletedTask;
        }
    }
}
