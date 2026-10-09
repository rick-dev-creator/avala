using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Canvases;

public interface ICanvasSurfaceViewModel
{
    string Title { get; }

    string MediaLabel { get; }

    string StatusText { get; }

    bool IsStreaming { get; }

    CanvasRendering Shown { get; }

    int VersionCount { get; }

    string VersionText { get; }

    bool HasVersions { get; }

    bool IsFollowingLatest { get; }

    bool IsOpen { get; }

    IRelayCommand PreviousVersionCommand { get; }

    IRelayCommand NextVersionCommand { get; }

    IRelayCommand LatestVersionCommand { get; }

    IRelayCommand OpenCommand { get; }

    IRelayCommand CloseCommand { get; }
}
