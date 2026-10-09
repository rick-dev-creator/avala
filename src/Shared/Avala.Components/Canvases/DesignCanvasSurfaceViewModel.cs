using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Canvases;

public sealed class DesignCanvasSurfaceViewModel : ICanvasSurfaceViewModel
{
    public const string RoundingPath =
        "flowchart LR\n  A[Amount ¥1,000] --> B[ToMinor ×100]\n  B --> C[ApplyTax · round]\n  C --> D[FromMinor ÷100 · round]\n  D -. rounds a second time .-> C";

    public string Title => "Rounding path";

    public string MediaLabel => "Mermaid";

    public string StatusText => string.Empty;

    public bool IsStreaming => false;

    public CanvasRendering Shown { get; } = new(CanvasMediaTypes.Mermaid, RoundingPath, true, 3);

    public int VersionCount => 3;

    public string VersionText => "3 of 3";

    public bool HasVersions => true;

    public bool IsFollowingLatest => true;

    public bool IsOpen => false;

    public IRelayCommand PreviousVersionCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand NextVersionCommand { get; } = new RelayCommand(() => { }, () => false);

    public IRelayCommand LatestVersionCommand { get; } = new RelayCommand(() => { }, () => false);

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand CloseCommand { get; } = new RelayCommand(() => { }, () => false);
}
