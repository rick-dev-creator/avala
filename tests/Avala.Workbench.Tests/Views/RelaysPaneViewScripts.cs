using Avala.Components.UI.Graphs;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class RelaysPaneViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePaneDrawsOneLanePerConnectionNamesTheTechniqueAndListsTheJourneyAsync() =>
        ui.RunAsync(() =>
        {
            var pane = new DesignRelaysPaneViewModel();
            var view = Screen.Show(pane, ThemeVariant.Dark, 440, 860);
            var track = view.All<RelayTrack>().Single();

            Assert.Equal(("3 connections · 2 handoffs", "Running on claude-max", "Elastic Harness Chaining"), (view.TextOf("Caption"), view.TextOf("Headline"), view.TextOf("Pattern")));
            Assert.Equal(3, track.Route.Lanes.Count);
            Assert.Equal(3, view.All<Border>().Count(lane => lane.Name == "Lane"));
            Assert.Equal(3, view.All<Rectangle>().Count(stop => stop.Name == "Stop"));
        }, Cancellation);

    [Fact]
    public Task AWaitingJourneyTurnsTheStatusToAttentionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRelaysPaneViewModel(waiting: true), ThemeVariant.Dark, 440, 860);

            Assert.Contains(view.All<UserControl>(), control => control.Classes.Contains("waiting"));
            Assert.Equal("Waiting for claude-work to reset", view.TextOf("Headline"));
        }, Cancellation);

    [Fact]
    public Task ThePaneClosesFromItsHeaderAsync() =>
        ui.RunAsync(() =>
        {
            var pane = new DesignRelaysPaneViewModel();
            var closed = 0;
            var view = Screen.Show(new ClosablePane(pane, () => closed++), ThemeVariant.Dark, 440, 600);

            view.Click("Close");

            Assert.Equal(1, closed);
        }, Cancellation);

    private sealed class ClosablePane(DesignRelaysPaneViewModel shown, Action close) : IRelaysPaneViewModel
    {
        public Avala.Workbench.Presenting.IReadProblemViewModel ReadProblem => shown.ReadProblem;

        public bool HasJourney => shown.HasJourney;

        public string Caption => shown.Caption;

        public string Headline => shown.Headline;

        public string Subline => shown.Subline;

        public bool IsWaiting => shown.IsWaiting;

        public Avala.Components.Graphs.RelayCourse Course => shown.Course;

        public IReadOnlyList<IRelayLaneViewModel> Lanes => shown.Lanes;

        public IReadOnlyList<IRelayLegViewModel> Legs => shown.Legs;

        public string Began => shown.Began;

        public string Latest => shown.Latest;

        public string Footer => shown.Footer;

        public bool IsOpen => true;

        public CommunityToolkit.Mvvm.Input.IRelayCommand CloseCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand(close);
    }
}
