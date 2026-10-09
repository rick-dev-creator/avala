using System.Diagnostics;
using Avala.Host.Composition;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class SmokeRunTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Executable => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Avala.Host.exe" : "Avala.Host");

    [Fact]
    public async Task TheSmokeRunComposesShowsTheMainWindowWithoutADisplayAndExitsCleanlyAsync()
    {
        await using var data = new TemporaryFolder();

        var (exitCode, output) = await RunAsync(plugins.Directory, data.Path, SmokeRun.Flag);

        Assert.Equal(0, exitCode);
        Assert.Matches($"^Avala {CompositionRoot.Build.Version} composed [1-9][0-9]* pages and showed its main window\\.$", output);
    }

    [Fact]
    public async Task TheSmokeRunFailsWhenNoPluginIsLoadedAsync()
    {
        await using var data = new TemporaryFolder();
        await using var empty = new TemporaryFolder();

        var (exitCode, output) = await RunAsync(empty.Path, data.Path, SmokeRun.Flag);

        Assert.Equal(2, exitCode);
        Assert.Equal("Avala started without any page: no plugin was loaded.", output);
    }

    [Fact]
    public async Task TheVersionFlagPrintsTheVersionAndTheCommitAsync()
    {
        await using var data = new TemporaryFolder();

        var (exitCode, output) = await RunAsync(plugins.Directory, data.Path, Program.VersionFlag);

        Assert.Equal(0, exitCode);
        Assert.Equal(Program.VersionLine, output);
        Assert.Matches("^Avala [0-9]+\\.[0-9]+\\.[0-9]+.* \\([0-9a-f]{40}\\)$", output);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string pluginFolder, string dataFolder, string flag)
    {
        var start = new ProcessStartInfo(Executable)
        {
            ArgumentList = { flag },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["AVALA_PLUGINS_PATH"] = pluginFolder,
                ["AVALA_DATA_PATH"] = dataFolder,
                ["DISPLAY"] = string.Empty,
                ["WAYLAND_DISPLAY"] = string.Empty,
            },
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(Cancellation);
        var error = process.StandardError.ReadToEndAsync(Cancellation);
        await process.WaitForExitAsync(Cancellation);

        return (process.ExitCode, ((await output).Trim() + (await error).Trim()).Trim());
    }
}
