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
    public bool IsFramed => true;

    public double FrameWidth => Device.Phone.Width;

    public double FrameHeight => Device.Phone.Height;

    public string Label => PreviewPhrases.Device(Device.Phone.Name, PreviewPhrases.Size(Device.Phone.Width, Device.Phone.Height));

    public IReadOnlyList<DeviceChoice> Choices { get; } =
    [
        new(PreviewPhrases.Responsive, PreviewPhrases.FitsThePane, false, new RelayCommand(() => { })),
        .. Device.All.Select(device => new DeviceChoice(device.Name, PreviewPhrases.Size(device.Width, device.Height), device == Device.Phone, new RelayCommand(() => { }))),
    ];

    public IRelayCommand TurnCommand { get; } = new RelayCommand(() => { });
}
