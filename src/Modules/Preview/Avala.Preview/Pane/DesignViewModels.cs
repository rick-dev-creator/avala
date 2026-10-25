using Avala.Preview.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Preview.Pane;

internal sealed class DesignPreviewPaneViewModel : IPreviewPaneViewModel
{
    public bool IsOpen => true;

    public string Address => "localhost:24000";

    public string State => PreviewPhrases.Running;

    public bool IsLive => true;

    public bool AllowsNetwork => false;

    public IWebSurface? Surface => null;

    public IDeviceFrameViewModel Frame { get; } = new DesignDeviceFrameViewModel();

    public bool IsEmbedded => true;

    public string Notice => string.Empty;

    public bool HasRefusal => true;

    public string Refusal => "Open unpkg.com in your browser?";

    public IRelayCommand BackCommand => Unavailable;

    public IRelayCommand ReloadCommand => Idle;

    public IAsyncRelayCommand ToggleNetworkCommand => IdleAsync;

    public IAsyncRelayCommand OpenInBrowserCommand => IdleAsync;

    public IAsyncRelayCommand OpenRefusedCommand => IdleAsync;

    public IRelayCommand DismissRefusalCommand => Idle;

    public IRelayCommand CloseCommand => Idle;

    private static RelayCommand Idle { get; } = new(() => { });

    private static RelayCommand Unavailable { get; } = new(() => { }, () => false);

    private static AsyncRelayCommand IdleAsync { get; } = new(() => Task.CompletedTask);
}

internal sealed class DesignRunningChipViewModel : IRunningChipViewModel
{
    public bool IsShown => true;

    public string Text => "Running · localhost:24000";

    public string Lease => string.Empty;

    public bool IsUnleased => false;

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignRunScriptViewModel : IRunScriptViewModel
{
    public bool IsShown => true;

    public bool IsRunning => false;

    public string Label => PreviewPhrases.Run("dev");

    public string Problem => string.Empty;

    public bool HasProblem => false;

    public IAsyncRelayCommand ToggleCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignDeviceFrameViewModel : IDeviceFrameViewModel
{
    private static Device Phone => Device.Named("iPhone 16");

    public bool IsFramed => true;

    public bool ShowsDepth => false;

    public bool IsFlat => true;

    public double FrameWidth => Phone.Width;

    public double FrameHeight => Phone.Height;

    public DeviceInsets Insets => Phone.Insets;

    public double OuterWidth => Phone.Width + Phone.Insets.Left + Phone.Insets.Right;

    public double OuterHeight => Phone.Height + Phone.Insets.Top + Phone.Insets.Bottom;

    public bool IsIsland => true;

    public bool IsPunch => false;

    public bool IsClassic => false;

    public bool IsFold => false;

    public bool IsTablet => false;

    public bool IsTurned => false;

    public string Label => PreviewPhrases.Device(Phone.Name, PreviewPhrases.Size(Phone.Width, Phone.Height));

    public IReadOnlyList<DeviceChoice> Choices { get; } =
    [
        new(PreviewPhrases.Responsive, PreviewPhrases.FitsThePane, false, new RelayCommand(() => { })),
        .. Device.All.Select(device => new DeviceChoice(device.Name, PreviewPhrases.Size(device.Width, device.Height), device == Phone, new RelayCommand(() => { }))),
    ];

    public IRelayCommand TurnCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand ToggleDepthCommand { get; } = new RelayCommand(() => { });
}
