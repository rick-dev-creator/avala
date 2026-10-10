using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Presentation;
using Avala.Sdk.Processes;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Shell.Regions;
using Avala.Testing.UI;
using Avala.Verification.Contracts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodRepository
{
    public static string Repository(string folder) => Path.Combine(folder, "repository");

    public static string Data(string folder) => Path.Combine(folder, "data");

    public static async Task PrepareAsync(DogfoodJournal journal, string folder)
    {
        Directory.CreateDirectory(Repository(folder));
        Directory.CreateDirectory(Data(folder));
        await File.WriteAllTextAsync(Path.Combine(Repository(folder), "README.md"), "# Notes\n\nA small Markdown notes CLI, to be built.\n", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Repository(folder), ".gitignore"), "node_modules/\n", Cancellation);
        Directory.CreateDirectory(Path.Combine(Repository(folder), ".avala"));
        await File.WriteAllTextAsync(Path.Combine(Repository(folder), ".avala", "permissions.json"), DogfoodSettings.Permissions, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Repository(folder), ".avala", "checks.json"), Rehearsal.Length > 0 ? Rehearsed : Checks, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Repository(folder), ".avala", "budget.json"), Budget, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Repository(folder), ".avala", "jobs.json"), $$"""{ "connection": "{{Connection}}", "approval": "keep" }""", Cancellation);
        await GitAsync(folder, "init", "--quiet", "--initial-branch=main");
        await GitAsync(folder, "add", "--all");
        await GitAsync(folder, "-c", "user.name=Dogfood", "-c", "user.email=dogfood@localhost", "-c", "commit.gpgsign=false", "commit", "--quiet", "--message", "Start the notes CLI with Avala's rules");
        var connections = new JsonObject
        {
            ["default"] = Connection,
            ["connections"] = new JsonArray(new JsonObject
            {
                ["name"] = Connection,
                ["provider"] = Rehearsal.Length > 0 ? "simulator" : "claude-code",
                ["settings"] = Rehearsal.Length > 0
                    ? new JsonObject { ["transcripts"] = Path.Combine(folder, "transcripts") }
                    : new JsonObject { ["transcripts"] = Path.Combine(folder, "transcripts"), ["model"] = Model },
            }),
        };
        if (Rehearsal.Length == 0)
        {
            connections["connections"]![0]!["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login };
        }

        await File.WriteAllTextAsync(Path.Combine(Data(folder), "connections.json"), connections.ToJsonString(), Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Data(folder), "recording.json"), """{ "enabled": true }""", Cancellation);
        await journal.NoteAsync($"Prepared {Repository(folder)} with .avala/permissions.json, checks.json, budget.json and jobs.json; connection {Connection} on {Login}");
    }

    private static async Task GitAsync(string folder, params string[] arguments)
    {
        using var git = Process.Start(new ProcessStartInfo("git", ["-C", Repository(folder), .. arguments]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        await git.WaitForExitAsync(Cancellation);
        Assert.Equal(0, git.ExitCode);
    }
}
