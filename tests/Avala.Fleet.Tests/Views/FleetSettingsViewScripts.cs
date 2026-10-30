using Avala.Fleet.Settings;
using Avala.Testing.UI;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

[assembly: AssemblyFixture(typeof(HeadlessUi))]

namespace Avala.Fleet.Tests.Views;

internal static class Screen
{
    private static bool registered;

    public static ViewScript Show(object viewModel)
    {
        if (!registered)
        {
            new FleetPlugin().RegisterViews(HeadlessApp.Views);
            registered = true;
        }

        return ViewScript.Show(viewModel);
    }
}

public sealed class FleetSettingsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task NotConnectedTheSectionAsksForTheAddressAndTheKeyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetSettingsViewModel { IsConnected = false });

            Assert.Equal((true, true, false, '•'), (view.Shows("ServerBox"), view.Shows("ConnectButton"), view.Shows("DisconnectButton"), view.Find<TextBox>("KeyBox").PasswordChar));
        }, Cancellation);

    [Fact]
    public Task ConnectedTheSectionShowsTheStateAndItsActionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetSettingsViewModel());

            Assert.Equal((false, true, true, true), (view.Shows("ConnectButton"), view.Shows("WaitingText"), view.Shows("TestButton"), view.Shows("DisconnectButton")));
        }, Cancellation);

    [Fact]
    public Task ConnectRunsTheCommandAsync() =>
        ui.RunAsync(() =>
        {
            var connected = 0;
            var view = Screen.Show(new DesignFleetSettingsViewModel
            {
                IsConnected = false,
                ConnectCommand = new AsyncRelayCommand(() =>
                {
                    connected++;

                    return Task.CompletedTask;
                }),
            });

            view.Click("ConnectButton");

            Assert.Equal(1, connected);
        }, Cancellation);

    [Fact]
    public Task AProblemAndRejectionsAreShownAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetSettingsViewModel { Problem = "The server did not answer" });

            Assert.Equal((true, 1), (view.Shows("ProblemText"), view.Find<ItemsControl>("Rejections").ItemCount));
        }, Cancellation);

    [Fact]
    public Task AServerThatStoppedAnsweringIsShownAsync() =>
        ui.RunAsync(() =>
        {
            var quiet = Screen.Show(new DesignFleetSettingsViewModel());
            var unanswered = Screen.Show(new DesignFleetSettingsViewModel { Unanswered = "The server has not answered since 12:00; Avala keeps trying" });

            Assert.Equal((false, true), (quiet.Shows("UnansweredText"), unanswered.Shows("UnansweredText")));
        }, Cancellation);
}

public sealed class FleetRejectionRowViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARowShowsWhenWhatAndWhyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFleetRejectionRowViewModel());

            Assert.Equal(("usageReported", "a cost has at most 6 decimals"), (view.Find<TextBlock>("KindText").Text, view.Find<TextBlock>("ReasonText").Text));
        }, Cancellation);
}
