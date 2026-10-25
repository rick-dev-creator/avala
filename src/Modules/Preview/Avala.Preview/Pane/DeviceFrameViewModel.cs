using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Preview.Pane;

internal interface IDeviceFrameViewModel
{
    bool IsFramed { get; }

    double FrameWidth { get; }

    double FrameHeight { get; }

    double Bezel { get; }

    double OuterWidth { get; }

    double OuterHeight { get; }

    bool IsPhone { get; }

    bool IsAndroid { get; }

    bool IsTablet { get; }

    bool IsTurned { get; }

    string Label { get; }

    IReadOnlyList<DeviceChoice> Choices { get; }

    IRelayCommand TurnCommand { get; }
}

internal sealed record DeviceChoice(string Name, string Size, bool IsChosen, IRelayCommand Choose);

[INotifyPropertyChanged]
internal sealed partial class DeviceFrameViewModel : IDeviceFrameViewModel
{
    private Option<Device> chosen;
    private bool turned;

    public DeviceFrameViewModel() => Choices = Listed();

    public bool IsFramed => chosen.IsSome;

    public double FrameWidth => Shown.Match(device => (double)device.Width, () => double.PositiveInfinity);

    public double FrameHeight => Shown.Match(device => (double)device.Height, () => double.PositiveInfinity);

    public double Bezel => chosen.Match(device => (double)device.Bezel, () => 0d);

    public double OuterWidth => FrameWidth + (2 * Bezel);

    public double OuterHeight => FrameHeight + (2 * Bezel);

    public bool IsPhone => Is(DeviceShape.Phone);

    public bool IsAndroid => Is(DeviceShape.Android);

    public bool IsTablet => Is(DeviceShape.Tablet);

    public bool IsTurned => chosen.IsSome && turned;

    public string Label => Shown.Match(device => PreviewPhrases.Device(device.Name, Size(device)), () => PreviewPhrases.Responsive);

    [ObservableProperty]
    public partial IReadOnlyList<DeviceChoice> Choices { get; private set; }

    private Option<Device> Shown => chosen.Map(device => turned ? device.Turn() : device);

    private bool Is(DeviceShape shape) => chosen.Match(device => device.Shape == shape, () => false);

    private static string Size(Device device) => PreviewPhrases.Size(device.Width, device.Height);

    [RelayCommand(CanExecute = nameof(IsFramed))]
    private void Turn()
    {
        turned = !turned;
        Changed();
    }

    private void Choose(Option<Device> device)
    {
        chosen = device;
        turned = false;
        Choices = Listed();
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsFramed));
        OnPropertyChanged(nameof(FrameWidth));
        OnPropertyChanged(nameof(FrameHeight));
        OnPropertyChanged(nameof(Bezel));
        OnPropertyChanged(nameof(OuterWidth));
        OnPropertyChanged(nameof(OuterHeight));
        OnPropertyChanged(nameof(IsPhone));
        OnPropertyChanged(nameof(IsAndroid));
        OnPropertyChanged(nameof(IsTablet));
        OnPropertyChanged(nameof(IsTurned));
        OnPropertyChanged(nameof(Label));
        TurnCommand.NotifyCanExecuteChanged();
    }

    private List<DeviceChoice> Listed() =>
    [
        new(PreviewPhrases.Responsive, PreviewPhrases.FitsThePane, chosen.IsNone, new RelayCommand(() => Choose(Option<Device>.None))),
        .. Device.All.Select(device => new DeviceChoice(device.Name, Size(device), chosen == Option<Device>.Some(device), new RelayCommand(() => Choose(device)))),
    ];
}
