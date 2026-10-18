using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Zooming;

public interface IZoomViewModel
{
    double Scale { get; set; }

    bool IsFitted { get; set; }

    double Minimum { get; }

    double Maximum { get; }

    string Percent { get; }

    IRelayCommand ZoomInCommand { get; }

    IRelayCommand ZoomOutCommand { get; }

    IRelayCommand FitCommand { get; }

    IRelayCommand ActualSizeCommand { get; }
}
