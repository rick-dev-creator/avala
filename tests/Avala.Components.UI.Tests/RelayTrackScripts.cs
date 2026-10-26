using Avala.Components.Graphs;
using Avala.Components.UI.Graphs;
using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class RelayTrackScripts(HeadlessUi ui)
{
    private static readonly RelayCourse EveryStop = new(
        2,
        [
            new RelayStretch(0, 0, 0.5, RelayStretchKind.Ran),
            new RelayStretch(1, 0.5, 1, RelayStretchKind.Running),
        ],
        [
            new RelayStop(0, 0.1, RelayStopKind.WindingDown),
            new RelayStop(0, 0.2, RelayStopKind.Capped),
            new RelayStop(0, 0.3, RelayStopKind.Compacting),
            new RelayStop(0, 0.4, RelayStopKind.Resumed),
            new RelayStop(1, 0.5, RelayStopKind.HandedOff),
        ]);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task WithReducedMotionTheWholeRouteShowsAtOnceAndHoldsStillAsync() =>
        ui.RunAsync(() =>
        {
            var track = new RelayTrack { Course = EveryStop };
            var view = ViewScript.Show(new Border(), new object());
            Motion.SetIsReduced(view.Window, true);
            view.Window.Content = track;
            view.Settle();

            Assert.Equal((1d, false, 5), (track.Revealed, track.IsMoving, track.Route.Markers.Count));
        }, Cancellation);
}
