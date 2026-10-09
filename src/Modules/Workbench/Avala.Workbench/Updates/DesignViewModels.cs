using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Updates;

internal sealed class DesignUpdateViewModel : IUpdateViewModel
{
    public string Version => "0.9.0-beta.1";

    public string Commit => "5c371e6a1b2d";

    public string Status { get; init; } = "Version 0.9.0 is available.";

    public bool IsAvailable { get; init; } = true;

    public IAsyncRelayCommand CheckCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenReleaseCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public void Refresh()
    {
    }
}

internal sealed class DesignUpdateNoticeViewModel : IUpdateNoticeViewModel
{
    public bool IsShown => true;

    public string Text => "Version 0.9.0 is available";

    public string Note { get; init; } = string.Empty;

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
