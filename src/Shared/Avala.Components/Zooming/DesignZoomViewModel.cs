using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Zooming;

public sealed class DesignZoomViewModel : IZoomViewModel
{
    private readonly ZoomViewModel zoom = new() { Scale = 0.8 };

    public double Scale
    {
        get => zoom.Scale;
        set => zoom.Scale = value;
    }

    public bool IsFitted
    {
        get => zoom.IsFitted;
        set => zoom.IsFitted = value;
    }

    public double Minimum => zoom.Minimum;

    public double Maximum => zoom.Maximum;

    public string Percent => zoom.Percent;

    public IRelayCommand ZoomInCommand => zoom.ZoomInCommand;

    public IRelayCommand ZoomOutCommand => zoom.ZoomOutCommand;

    public IRelayCommand FitCommand => zoom.FitCommand;

    public IRelayCommand ActualSizeCommand => zoom.ActualSizeCommand;
}
