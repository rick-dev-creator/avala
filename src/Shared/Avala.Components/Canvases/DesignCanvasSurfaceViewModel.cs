using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Canvases;

public sealed class DesignCanvasSurfaceViewModel : ICanvasSurfaceViewModel
{
    public const string RoundingPath = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 560 120" font-family="Inter, sans-serif" font-size="13">
          <defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0 10 5 0 10z" fill="#80808A"/></marker></defs>
          <g fill="none" stroke="#5A5A63" stroke-width="1.2">
            <rect x="10" y="40" width="110" height="40" rx="8"/><rect x="160" y="40" width="110" height="40" rx="8"/>
            <rect x="310" y="40" width="110" height="40" rx="8"/><rect x="460" y="40" width="90" height="40" rx="8" stroke="#EF6461" stroke-dasharray="4 3"/>
          </g>
          <g stroke="#80808A" stroke-width="1.2" marker-end="url(#arrow)"><path d="M120 60h38"/><path d="M270 60h38"/><path d="M420 60h38"/></g>
          <g fill="#EDEDEF" text-anchor="middle"><text x="65" y="64">Amount ¥1,000</text><text x="215" y="64">ToMinor ×100</text><text x="365" y="64">ApplyTax</text><text x="505" y="64">FromMinor</text></g>
          <text x="505" y="100" fill="#EF6461" text-anchor="middle" font-size="11">rounds twice</text>
        </svg>
        """;

    public string Title => "Rounding path";

    public string MediaLabel => "SVG";

    public string StatusText => string.Empty;

    public bool IsStreaming => false;

    public CanvasRendering Shown { get; } = new(CanvasMediaTypes.Svg, RoundingPath, true, 3);

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
