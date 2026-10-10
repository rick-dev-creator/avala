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

internal static class DogfoodLeftovers
{
    public static async Task LateEventsAsync(DogfoodJournal journal, string data)
    {
        var logs = new AvalaPaths(data).Logs;
        var files = Directory.Exists(logs) ? Directory.GetFiles(logs) : [];
        var lines = (await Task.WhenAll(files.Select(file => File.ReadAllLinesAsync(file, Cancellation)))).SelectMany(read => read).ToList();
        var late = lines.Where(line => line.Contains("published after the event bus stopped", StringComparison.Ordinal)).ToList();
        await journal.NoteAsync(late.Count == 0
            ? $"SHUTDOWN: none of the {lines.Count} lines in {files.Length} log files says an event was published after the event bus stopped"
            : $"FINDING: {late.Count} events were published after the event bus stopped: {string.Join(" ⏎ ", late.Take(10))}");
    }

    public static async Task<List<int>> OrphansAsync()
    {
        List<int> found = [];

        foreach (var process in Process.GetProcessesByName("sleep"))
        {
            string line;

            try
            {
                line = await File.ReadAllTextAsync($"/proc/{process.Id}/cmdline", Cancellation);
            }
            catch (IOException)
            {
                continue;
            }

            if (line.Replace('\0', ' ').Trim() == SlowCheck)
            {
                found.Add(process.Id);
            }
        }

        return found;
    }

    public static async Task RecordPermissionAsync(DogfoodJournal journal, string report, PolicyDecision decision)
    {
        var chained = decision.Kind == ItemKind.Command && DogfoodPolicy.Chained(decision.Target);
        var rule = decision.Rule.Match(chosen => $"{chosen.Origin} rule '{chosen.Name}'", () => "no rule");
        var line = $"{decision.Kind} {decision.Answer} by {rule}, delivery {decision.Delivery}, chained {chained}: {decision.Target.ReplaceLineEndings(" ⏎ ")}";
        await File.AppendAllTextAsync(Path.Combine(report, "permissions.log"), $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}", Cancellation);

        if (chained && decision.Answer == PolicyAnswer.Allow && decision.Delivery == DecisionDelivery.Answered && decision.Rule.Match(chosen => chosen.Origin == RuleOrigin.Repository, () => false))
        {
            await journal.NoteAsync($"FINDING: a chained command was auto-allowed by a repository rule: {line}");
        }
    }

    public static List<Type> EventTypes() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("Avala.", StringComparison.Ordinal) == true)
            .SelectMany(Loadable)
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } && typeof(IIntegrationEvent).IsAssignableFrom(type))
            .Distinct()];

    private static IEnumerable<Type> Loadable(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }
}
