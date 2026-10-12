using Avala.Sdk;
using Avala.Shell;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Avala.Host.Composition;

internal static class SmokeRun
{
    public const string Flag = "--smoke";

    public const string DataVariable = "AVALA_DATA_PATH";

    public static bool IsRequested(IEnumerable<string> args) => args.Contains(Flag, StringComparer.Ordinal);

    public static Option<string> TemporaryDataFolder() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DataVariable))
            ? Path.Combine(Path.GetTempPath(), $"avala-smoke-{Guid.NewGuid():N}")
            : Option<string>.None;

    public static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task PassAsync(IClassicDesktopStyleApplicationLifetime desktop, Task showing, Task started, Task running, ShellViewModel shell)
    {
        var exitCode = await VerdictAsync(desktop, showing, started, running, shell);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(exitCode), DispatcherPriority.Background);
    }

    private static async Task<int> VerdictAsync(IClassicDesktopStyleApplicationLifetime desktop, Task showing, Task started, Task running, ShellViewModel shell)
    {
        try
        {
            await showing;
            await StartedAsync(started, running);
            var passed = shell.Pages.Count > 0 && desktop.MainWindow is ShellView;
            Console.WriteLine(passed
                ? $"Avala {CompositionRoot.Build.Version} composed {shell.Pages.Count} pages and showed its main window."
                : "Avala started without any page: no plugin was loaded.");

            return passed ? 0 : 2;
        }
        catch (Exception failure)
        {
            await Console.Error.WriteLineAsync($"Avala failed to start: {failure}");

            return 1;
        }
    }

    private static async Task StartedAsync(Task started, Task running)
    {
        if (await Task.WhenAny(started, running) == running)
        {
            await running;

            throw new InvalidOperationException("The runtime stopped before its startup completed.");
        }

        await started;
    }
}
