using Avala.Components.QrCodes;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Remoting;

internal sealed class DesignRemoteControlViewModel : IRemoteControlViewModel
{
    public const string SessionLink = "https://claude.ai/code/session_01XLwi39hWkad8C1AaNLEP69";

    public bool IsShown { get; init; } = true;

    public bool IsOn { get; init; } = true;

    public bool IsConnected { get; init; } = true;

    public bool IsOpen { get; set; }

    public string Status { get; init; } = RemotePhrases.Connected;

    public string Link { get; init; } = SessionLink;

    public bool HasLink => Link.Length > 0;

    public QrModules Code => QrModules.Of(Link);

    public string Problem { get; init; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public IRelayCommand ToggleOpenCommand { get; init; } = new RelayCommand(() => { });

    public IAsyncRelayCommand ToggleCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CopyLinkCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenLinkCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
