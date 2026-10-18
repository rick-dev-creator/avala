using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Zooming;

[INotifyPropertyChanged]
public sealed partial class ZoomViewModel : IZoomViewModel
{
    public const double Step = 1.25;

    private const double Tolerance = 0.005;

    public double Minimum => 0.25;

    public double Maximum => 4;

    private double Lowest { get; set; } = 0.25;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent))]
    [NotifyCanExecuteChangedFor(nameof(ZoomInCommand), nameof(ZoomOutCommand), nameof(ActualSizeCommand))]
    public partial double Scale { get; set; } = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FitCommand))]
    public partial bool IsFitted { get; set; } = true;

    public string Percent => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(Scale * 100)}%");

    public void Fit() => IsFitted = true;

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => Zoom(Math.Min(Maximum, Scale * Step));

    private bool CanZoomIn() => Scale < Maximum - Tolerance;

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => Zoom(Math.Max(Math.Min(Lowest, Scale), Scale / Step));

    private bool CanZoomOut() => Scale > Lowest + Tolerance;

    partial void OnScaleChanged(double value)
    {
        if (IsFitted)
        {
            Lowest = Math.Min(Minimum, value);
        }
    }

    [RelayCommand(CanExecute = nameof(CanFitAgain))]
    private void FitAgain() => Fit();

    private bool CanFitAgain() => !IsFitted;

    public IRelayCommand FitCommand => FitAgainCommand;

    [RelayCommand(CanExecute = nameof(CanShowActualSize))]
    private void ActualSize() => Zoom(1);

    private bool CanShowActualSize() => Math.Abs(Scale - 1) > Tolerance;

    private void Zoom(double scale)
    {
        IsFitted = false;
        Scale = scale;
    }
}
