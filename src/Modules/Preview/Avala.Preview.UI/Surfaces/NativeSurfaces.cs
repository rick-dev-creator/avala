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

    private static SurfaceEmbedding Detect()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
        {
            return SurfaceEmbedding.None;
        }

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
