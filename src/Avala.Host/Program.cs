using Avala.Host.Composition;
using Avala.Sdk;
using Avalonia;
using Avalonia.Headless;

namespace Avala.Host;

internal static class Program
{
    public const string VersionFlag = "--version";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is [VersionFlag])
        {
            Console.WriteLine(VersionLine);

            return 0;
        }

        if (!SmokeRun.IsRequested(args))
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        var temporary = SmokeRun.TemporaryDataFolder();
        _ = temporary.Match(
            folder =>
            {
                Environment.SetEnvironmentVariable(SmokeRun.DataVariable, folder);
                return true;
            },
            () => false);

        var exitCode = AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args);
        _ = temporary.Match(SmokeRun.TryDelete, () => false);

        return exitCode;
    }

    public static string VersionLine =>
        $"Avala {CompositionRoot.Build.Version} ({CompositionRoot.Build.Commit.Match(commit => commit, () => AvalaBuild.Unknown)})";

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
