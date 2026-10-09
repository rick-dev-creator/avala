using Avala.Sdk;
using Avala.Shell;
using Avalonia.Controls.ApplicationLifetimes;

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

    public static async Task PassAsync(IClassicDesktopStyleApplicationLifetime desktop, Task showing, IAsyncEnumerable<StartupCompleted> started, ShellViewModel shell)
    {
        try
        {
            await showing;
            await started.FirstAsync();
            var passed = shell.Pages.Count > 0 && desktop.MainWindow is ShellView;
            Console.WriteLine(passed
                ? $"Avala {CompositionRoot.Build.Version} composed {shell.Pages.Count} pages and showed its main window."
                : "Avala started without any page: no plugin was loaded.");
            desktop.Shutdown(passed ? 0 : 2);
        }
        catch (Exception failure)
        {
            await Console.Error.WriteLineAsync($"Avala failed to start: {failure}");
            desktop.Shutdown(1);
        }
    }
}
