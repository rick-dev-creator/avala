using Avala.Preview.Contracts;
using Avala.Sdk;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace Avala.Preview.UI.Surfaces;

internal sealed class NativeSurfaces : IWebSurfaces
{
    private const string DmabufVariable = "WEBKIT_DISABLE_DMABUF_RENDERER";

    public static void Prepare()
    {
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable(DmabufVariable) is null)
        {
            Environment.SetEnvironmentVariable(DmabufVariable, "1");
            _ = SetNative(DmabufVariable, "1", 0);
        }
    }

    private readonly Lazy<SurfaceEmbedding> embedding = new(Detect);

    public SurfaceEmbedding Embedding => embedding.Value;

    public IWebSurface Open(SurfaceRules rules) => new NativeSurface(rules, Embedding);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "setenv", CharSet = System.Runtime.InteropServices.CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int SetNative(string name, string value, int overwrite);

    private static SurfaceEmbedding Detect() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime ? Best(Installed()) : SurfaceEmbedding.None;

    private static List<WebViewEmbeddingScenario> Installed() =>
        [.. Candidates().SelectMany(type => Describe(type).Match<DetailedWebViewAdapterInfo[]>(info => [info], () => [])).Where(Usable).Select(info => info.SupportedScenarios)];

    private static bool Usable(DetailedWebViewAdapterInfo info) => info.IsSupported && info.IsInstalled;

    private static SurfaceEmbedding Best(List<WebViewEmbeddingScenario> installed) =>
        installed.Exists(Embeds) ? SurfaceEmbedding.Embedded
        : installed.Exists(scenarios => scenarios.HasFlag(WebViewEmbeddingScenario.NativeDialog)) ? SurfaceEmbedding.Window
        : SurfaceEmbedding.None;

    private static bool Embeds(WebViewEmbeddingScenario scenarios) =>
        scenarios.HasFlag(WebViewEmbeddingScenario.NativeControlHost) || scenarios.HasFlag(WebViewEmbeddingScenario.OffscreenRenderer);

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
