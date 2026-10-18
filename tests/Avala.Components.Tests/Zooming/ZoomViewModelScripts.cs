using Avala.Components.Zooming;
using Avala.Testing;

namespace Avala.Components.Tests.Zooming;

public sealed class ZoomViewModelScripts
{
    [Fact]
    public void ZoomingInLeavesTheFitAndStepsTheScaleUpToItsMaximum() =>
        ViewModelScript.Given(new ZoomViewModel { Scale = 0.8 })
            .When(zoom => zoom.ZoomInCommand.Execute(null))
            .Then(zoom => Assert.Equal((1d, "100%", false, true), (zoom.Scale, zoom.Percent, zoom.IsFitted, zoom.FitCommand.CanExecute(null))))
            .When(zoom =>
            {
                for (var step = 0; step < 10; step++)
                {
                    zoom.ZoomInCommand.Execute(null);
                }
            })
            .Then(zoom => Assert.Equal((zoom.Maximum, false), (zoom.Scale, zoom.ZoomInCommand.CanExecute(null))));

    [Fact]
    public void ZoomingOutStepsTheScaleDownToItsMinimum() =>
        ViewModelScript.Given(new ZoomViewModel())
            .When(zoom => zoom.ZoomOutCommand.Execute(null))
            .Then(zoom => Assert.Equal((0.8, "80%"), (zoom.Scale, zoom.Percent)))
            .When(zoom =>
            {
                for (var step = 0; step < 10; step++)
                {
                    zoom.ZoomOutCommand.Execute(null);
                }
            })
            .Then(zoom => Assert.Equal((zoom.Minimum, false), (zoom.Scale, zoom.ZoomOutCommand.CanExecute(null))));

    [Fact]
    public void ActualSizeShowsTheDrawingAtOneHundredPercentAndFitReturnsToTheFit() =>
        ViewModelScript.Given(new ZoomViewModel { Scale = 0.62 })
            .Then(zoom => Assert.Equal((true, false, true), (zoom.ActualSizeCommand.CanExecute(null), zoom.FitCommand.CanExecute(null), zoom.IsFitted)))
            .When(zoom => zoom.ActualSizeCommand.Execute(null))
            .Then(zoom => Assert.Equal((1d, false, false), (zoom.Scale, zoom.IsFitted, zoom.ActualSizeCommand.CanExecute(null))))
            .When(zoom => zoom.FitCommand.Execute(null))
            .Then(zoom => Assert.Equal((true, false), (zoom.IsFitted, zoom.FitCommand.CanExecute(null))));
}
