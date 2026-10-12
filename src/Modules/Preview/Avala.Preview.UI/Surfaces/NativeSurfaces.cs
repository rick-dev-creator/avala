using Avala.Preview.Surfaces;
using Avala.Sdk;
using Avalonia.Platform;

namespace Avala.Preview.UI.Surfaces;

internal sealed class NativeSurfaces : IWebSurfaces
{
    private readonly Lazy<SurfaceEmbedding> embedding = new(Detect);

    public SurfaceEmbedding Embedding => embedding.Value;

    public IWebSurface Open(SurfaceRules rules) => new NativeSurface(rules, Embedding);

    private static SurfaceEmbedding Detect()
    {
        var installed = Candidates().SelectMany(type => Describe(type).Match<DetailedWebViewAdapterInfo[]>(info => [info], () => [])).Where(info => info.IsSupported && info.IsInstalled).ToList();

        return installed.Any(info => info.SupportedScenarios.HasFlag(WebViewEmbeddingScenario.NativeControlHost) || info.SupportedScenarios.HasFlag(WebViewEmbeddingScenario.OffscreenRenderer))
            ? SurfaceEmbedding.Embedded
            : installed.Any(info => info.SupportedScenarios.HasFlag(WebViewEmbeddingScenario.NativeDialog))
                ? SurfaceEmbedding.Window
                : SurfaceEmbedding.None;
    }

    private static WebViewAdapterType[] Candidates() =>
        OperatingSystem.IsWindows() ? [WebViewAdapterType.WebView2]
        : OperatingSystem.IsMacOS() ? [WebViewAdapterType.WkWebView]
        : OperatingSystem.IsLinux() ? [WebViewAdapterType.WpeWebKit, WebViewAdapterType.WebKitGtk]
        : [];

    private static Option<DetailedWebViewAdapterInfo> Describe(WebViewAdapterType type)
    {
        try
        {
            return WebViewAdapterInfo.GetAdapterInfo(type);
        }
        catch (Exception exception) when (exception is TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or PlatformNotSupportedException)
        {
            return Option<DetailedWebViewAdapterInfo>.None;
        }
    }
}
