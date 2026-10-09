using Avala.Components.Canvases;
using Avala.Testing;

namespace Avala.Components.Tests.Canvases;

public sealed class CanvasSurfaceViewModelScripts
{
    private const string Svg = "image/svg+xml";

    [Fact]
    public void TheFirstSnapshotIsShownAsTheOnlyVersionWhileTheCanvasStreams() =>
        ViewModelScript.Given(new CanvasSurfaceViewModel())
            .Then(surface => Assert.True(surface.Shown.IsNothing))
            .When(surface => surface.Show(Streaming("<svg")))
            .ThenNotified(nameof(CanvasSurfaceViewModel.Title), nameof(CanvasSurfaceViewModel.Shown), nameof(CanvasSurfaceViewModel.VersionText))
            .Then(surface => Assert.Equal(
                ("Request flow", "SVG", "Drawing", true, "<svg", false, "1 of 1", false),
                (surface.Title, surface.MediaLabel, surface.StatusText, surface.IsStreaming, surface.Shown.Content, surface.Shown.IsFinal, surface.VersionText, surface.HasVersions)));

    [Fact]
    public void EverySnapshotThatChangesTheDrawingBecomesAVersionAndTheSurfaceFollowsTheLatest() =>
        ViewModelScript.Given(new CanvasSurfaceViewModel())
            .When(surface =>
            {
                surface.Show(Streaming("<svg"));
                surface.Show(Streaming("<svg><rect"));
                surface.Show(Streaming("<svg><rect/></svg>"));
            })
            .Then(surface => Assert.Equal(
                ("<svg><rect/></svg>", "3 of 3", true, true),
                (surface.Shown.Content, surface.VersionText, surface.HasVersions, surface.IsFollowingLatest)))
            .Then(surface => Assert.Equal((true, false, false), (surface.PreviousVersionCommand.CanExecute(null), surface.NextVersionCommand.CanExecute(null), surface.LatestVersionCommand.CanExecute(null))));

    [Fact]
    public void ASnapshotThatOnlyClosesTheCanvasFinishesTheLatestVersionWithoutAddingOne() =>
        ViewModelScript.Given(new CanvasSurfaceViewModel())
            .When(surface =>
            {
                surface.Show(Streaming("<svg/>"));
                surface.Show(Streaming("<svg/>"));
                surface.Show(Closed("<svg/>", CanvasPhase.Completed));
            })
            .Then(surface => Assert.Equal(
                (1, true, string.Empty, false),
                (surface.VersionCount, surface.Shown.IsFinal, surface.StatusText, surface.IsStreaming)));

    [Fact]
    public void SteppingBackShowsTheEarlierVersionAndOffersTheWayForward() =>
        ViewModelScript.Given(Drawn("a", "ab", "abc"))
            .Invoke(nameof(CanvasSurfaceViewModel.PreviousVersionCommand))
            .Invoke(nameof(CanvasSurfaceViewModel.PreviousVersionCommand))
            .Then(surface => Assert.Equal(("a", "1 of 3", false), (surface.Shown.Content, surface.VersionText, surface.IsFollowingLatest)))
            .Then(surface => Assert.Equal((false, true, true), (surface.PreviousVersionCommand.CanExecute(null), surface.NextVersionCommand.CanExecute(null), surface.LatestVersionCommand.CanExecute(null))))
            .Invoke(nameof(CanvasSurfaceViewModel.NextVersionCommand))
            .Then(surface => Assert.Equal(("ab", "2 of 3"), (surface.Shown.Content, surface.VersionText)));

    [Fact]
    public void ANewSnapshotKeepsTheVersionAPersonSteppedBackToUntilTheyReturnToTheLatest() =>
        ViewModelScript.Given(Drawn("a", "ab"))
            .Invoke(nameof(CanvasSurfaceViewModel.PreviousVersionCommand))
            .When(surface => surface.Show(Streaming("abc")))
            .Then(surface => Assert.Equal(("a", "1 of 3", false), (surface.Shown.Content, surface.VersionText, surface.IsFollowingLatest)))
            .Invoke(nameof(CanvasSurfaceViewModel.LatestVersionCommand))
            .When(surface => surface.Show(Streaming("abcd")))
            .Then(surface => Assert.Equal(("abcd", "4 of 4", true), (surface.Shown.Content, surface.VersionText, surface.IsFollowingLatest)));

    [Fact]
    public void OnlyTheMostRecentVersionsAreKeptAndTheOneShownSurvives()
    {
        var surface = Drawn("v1");
        surface.Show(Streaming("v2"));
        surface.PreviousVersionCommand.Execute(null);

        foreach (var version in Enumerable.Range(3, CanvasSurfaceViewModel.VersionLimit + 5))
        {
            surface.Show(Streaming($"v{version}"));
        }

        Assert.Equal((CanvasSurfaceViewModel.VersionLimit, "v1"), (surface.VersionCount, surface.Shown.Content));
        surface.LatestVersionCommand.Execute(null);
        Assert.Equal($"v{CanvasSurfaceViewModel.VersionLimit + 7}", surface.Shown.Content);
    }

    [Fact]
    public void OpeningShowsTheDrawingInAFocusedViewUntilItIsClosed() =>
        ViewModelScript.Given(new CanvasSurfaceViewModel())
            .Then(surface => Assert.False(surface.OpenCommand.CanExecute(null)))
            .When(surface => surface.Show(Closed("# Plan", CanvasPhase.Completed)))
            .Invoke(nameof(CanvasSurfaceViewModel.OpenCommand))
            .ThenNotified(nameof(CanvasSurfaceViewModel.IsOpen))
            .Then(surface => Assert.Equal((true, false, true), (surface.IsOpen, surface.OpenCommand.CanExecute(null), surface.CloseCommand.CanExecute(null))))
            .Invoke(nameof(CanvasSurfaceViewModel.CloseCommand))
            .Then(surface => Assert.Equal((false, true), (surface.IsOpen, surface.OpenCommand.CanExecute(null))));

    [Theory]
    [InlineData("text/markdown; charset=utf-8", "Markdown")]
    [InlineData("IMAGE/SVG+XML", "SVG")]
    [InlineData("text/vnd.mermaid", "Mermaid")]
    [InlineData("text/html", "HTML")]
    [InlineData("application/vnd.custom", "application/vnd.custom")]
    public void TheKindOfDrawingIsNamedForPeople(string mediaType, string label) =>
        ViewModelScript.Given(new CanvasSurfaceViewModel())
            .When(surface => surface.Show(new CanvasDraft("Notes", mediaType, "x", CanvasPhase.Completed)))
            .Then(surface => Assert.Equal(label, surface.MediaLabel));

    [Theory]
    [InlineData("Failed", "Failed")]
    [InlineData("Stopped", "Stopped")]
    [InlineData("Completed", "")]
    public void HowTheCanvasClosedReadsAsAWord(string phase, string status) =>
        ViewModelScript.Given(Drawn("a"))
            .When(surface => surface.Show(Closed("a", Enum.Parse<CanvasPhase>(phase))))
            .Then(surface => Assert.Equal((status, true), (surface.StatusText, surface.Shown.IsFinal)));

    [Fact]
    public void TheVersionsOfOneSurfaceFollowEachOtherButNeverAnotherSurfaces()
    {
        var first = Drawn("a", "ab");
        var other = Drawn("x", "xy", "xyz");
        var earlier = first.Shown;
        first.Show(Streaming("abc"));

        Assert.Equal((true, false, false), (first.Shown.Follows(earlier), earlier.Follows(first.Shown), other.Shown.Follows(earlier)));
    }

    [Fact]
    public void TheDesignTimeSurfaceIsTheBriefsSmallDiagramDrawnAsSvg()
    {
        var surface = new DesignCanvasSurfaceViewModel();

        Assert.Equal(("SVG", CanvasMediaTypes.Svg, true, true), (surface.MediaLabel, surface.Shown.Essence, surface.Shown.IsOffered, surface.HasVersions));
    }

    private static CanvasSurfaceViewModel Drawn(params string[] versions)
    {
        var surface = new CanvasSurfaceViewModel();

        foreach (var version in versions)
        {
            surface.Show(Streaming(version));
        }

        return surface;
    }

    private static CanvasDraft Streaming(string content) => new("Request flow", Svg, content, CanvasPhase.Streaming);

    private static CanvasDraft Closed(string content, CanvasPhase phase) => new("Request flow", Svg, content, phase);
}
