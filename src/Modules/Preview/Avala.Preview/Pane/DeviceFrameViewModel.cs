using Avala.Preview.Navigation;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Preview.Pane;

internal interface IDeviceFrameViewModel
{
    bool IsFramed { get; }

    bool ShowsDepth { get; }

    bool IsFlat { get; }

    double FrameWidth { get; }

    double FrameHeight { get; }

    DeviceInsets Insets { get; }

    double OuterWidth { get; }

    double OuterHeight { get; }

    bool IsIsland { get; }

    bool IsPunch { get; }

    bool IsClassic { get; }

    bool IsFold { get; }

    bool IsTablet { get; }

    bool IsTurned { get; }

    string Label { get; }

    IReadOnlyList<DeviceChoice> Choices { get; }

    IRelayCommand TurnCommand { get; }

    IRelayCommand ToggleDepthCommand { get; }
}

internal sealed record DeviceChoice(string Name, string Size, bool IsChosen, IRelayCommand Choose);

[INotifyPropertyChanged]
internal sealed partial class DeviceFrameViewModel : IDeviceFrameViewModel
{
    private Option<Device> chosen;
    private bool turned;
    private bool depth;

    public DeviceFrameViewModel() => Choices = Listed();

    public event EventHandler? Changed;

    public bool IsFramed => chosen.IsSome;

    public bool ShowsDepth => chosen.IsSome && depth;

    public bool IsFlat => Flat.IsSome;

    public Option<DeviceView> View => Shown.Map(device => new DeviceView(device.Model, device.Width, device.Height, turned, depth));

    public double FrameWidth => Flat.Match(device => (double)device.Width, () => double.PositiveInfinity);

    public double FrameHeight => Flat.Match(device => (double)device.Height, () => double.PositiveInfinity);

    public DeviceInsets Insets => Flat.Match(device => device.Insets, () => DeviceInsets.None);

    public double OuterWidth => FrameWidth + Insets.Left + Insets.Right;

    public double OuterHeight => FrameHeight + Insets.Top + Insets.Bottom;

    public bool IsIsland => Is(DeviceShape.Island);

    public bool IsPunch => Is(DeviceShape.Punch);

    public bool IsClassic => Is(DeviceShape.Classic);

    public bool IsFold => Is(DeviceShape.Fold);

    public bool IsTablet => Is(DeviceShape.Tablet);

    public bool IsTurned => Flat.IsSome && turned;

    public string Label => Shown.Match(device => PreviewPhrases.Device(device.Name, Size(device)), () => PreviewPhrases.Responsive);

    [ObservableProperty]
    public partial IReadOnlyList<DeviceChoice> Choices { get; private set; }

    private Option<Device> Shown => chosen.Map(device => turned ? device.Turn() : device);

    private Option<Device> Flat => depth ? Option<Device>.None : Shown;

    private bool Is(DeviceShape shape) => Flat.Match(device => device.Shape == shape, () => false);

    private static string Size(Device device) => PreviewPhrases.Size(device.Width, device.Height);

    [RelayCommand(CanExecute = nameof(IsFramed))]
    private void Turn()
    {
        turned = !turned;
        Announce();
    }

    [RelayCommand(CanExecute = nameof(IsFramed))]
    private void ToggleDepth()
    {
        depth = !depth;
        Announce();
    }

    private void Choose(Option<Device> device)
    {
        chosen = device;
        turned = false;
        depth = depth && device.IsSome;
        Choices = Listed();
        Announce();
    }

    private void Announce()
    {
        OnPropertyChanged(string.Empty);
        TurnCommand.NotifyCanExecuteChanged();
        ToggleDepthCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<DeviceChoice> Listed() =>
    [
        new(PreviewPhrases.Responsive, PreviewPhrases.FitsThePane, chosen.IsNone, new RelayCommand(() => Choose(Option<Device>.None))),
        .. Device.All.Select(device => new DeviceChoice(device.Name, Size(device), chosen == Option<Device>.Some(device), new RelayCommand(() => Choose(device)))),
    ];
}
