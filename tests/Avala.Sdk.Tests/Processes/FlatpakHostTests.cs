using System.Diagnostics;
using Avala.Sdk.Processes;

namespace Avala.Sdk.Tests.Processes;

public sealed class FlatpakHostTests
{
    [Fact]
    public void InsideFlatpakAProcessRunsOnTheHostInItsFolderWithTheVariablesItChanged()
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = "/home/ada/avala", RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment.Clear();
        info.Environment["PATH"] = "/app/bin:/usr/bin";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.ArgumentList.Add("status");
        info.ArgumentList.Add("--porcelain");
        var inherited = new Dictionary<string, string> { ["PATH"] = "/app/bin:/usr/bin", ["FLATPAK_ID"] = "io.github.rick_dev_creator.Avala" };

        var host = FlatpakHost.OnHost(info, inherited);

        Assert.Equal(FlatpakHost.Spawn, host.FileName);
        Assert.Equal(
            [
                "--host", "--watch-bus", "--directory=/home/ada/avala", "--env=GIT_TERMINAL_PROMPT=0", "--unset-env=FLATPAK_ID",
                "sh", "-lc", FlatpakHost.LoginShell, "sh", "git", "status", "--porcelain",
            ],
            host.ArgumentList);
        Assert.Equal((false, true, true, false), (host.RedirectStandardInput, host.RedirectStandardOutput, host.RedirectStandardError, host.UseShellExecute));
    }
}
