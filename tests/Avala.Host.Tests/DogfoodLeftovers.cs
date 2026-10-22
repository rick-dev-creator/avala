using System.Diagnostics;
using System.Reflection;
using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static class DogfoodLeftovers
{
    public static async Task LateEventsAsync(DogfoodJournal journal, DogfoodEvidence evidence, string data)
    {
        var logs = new AvalaPaths(data).Logs;
        var files = Directory.Exists(logs) ? Directory.GetFiles(logs) : [];
        var lines = (await Task.WhenAll(files.Select(file => File.ReadAllLinesAsync(file, Cancellation)))).SelectMany(read => read).ToList();
        var late = lines.Where(line => line.Contains("published after the event bus stopped", StringComparison.Ordinal)).ToList();
        var stops = lines.Count(line => line.Contains(DogfoodLog.Stopping, StringComparison.Ordinal));
        evidence.Note("late-events", late.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        evidence.Note("log-files", files.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await journal.NoteAsync(late.Count == 0
            ? $"SHUTDOWN: none of the {lines.Count} lines in {files.Length} log files of {logs}, which saw Avala stop {stops} time(s), says an event was published after the event bus stopped"
            : $"FINDING: {late.Count} events were published after the event bus stopped: {string.Join(" ⏎ ", late.Take(10))}");

        if (late.Count > 0)
        {
            evidence.Find($"{late.Count} events were published after the event bus stopped");
        }

        if (stops == 0)
        {
            await journal.NoteAsync($"FINDING: the log in {logs} never saw Avala stop, so it cannot say whether an event was published after the event bus stopped");
            evidence.Find($"The application's log in {logs} never saw Avala stop");
        }
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

    public static async Task RecordPermissionAsync(DogfoodJournal journal, PolicyDecision decision, DogfoodEvidence evidence)
    {
        var chained = decision.Kind == ItemKind.Command && DogfoodPolicy.Chained(decision.Target);
        var rule = decision.Rule.Match(chosen => $"{chosen.Origin} rule '{chosen.Name}'", () => "no rule");
        var line = $"{evidence.Name(decision.Job)} {decision.Kind} {decision.Answer} by {rule}, delivery {decision.Delivery}, parent {evidence.Name(decision.Parent)}, chained {chained}: {decision.Target}";
        await journal.LogAsync("permissions.log", line);

        if (chained && decision.Answer == PolicyAnswer.Allow && decision.Delivery == DecisionDelivery.Answered && decision.Rule.Match(chosen => chosen.Origin == RuleOrigin.Repository, () => false))
        {
            await journal.NoteAsync($"FINDING: a chained command was auto-allowed by a repository rule: {line}");
            evidence.Find($"A chained command was auto-allowed by a repository rule: {decision.Target}");
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
