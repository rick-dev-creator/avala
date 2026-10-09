using System.Collections.Immutable;
using System.Globalization;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal static class MacListings
{
    public static IReadOnlyList<string> ProcessArguments { get; } = ["-axEww", "-o", "pid=,ppid=,stat=,command="];

    public static IReadOnlyList<string> ListenerArguments { get; } = ["-nP", "-iTCP", "-sTCP:LISTEN", "-Fpn"];

    public static IReadOnlyList<int> Members(Option<string> processes, string marker, ImmutableHashSet<int> roots, int harness)
    {
        var candidates = Lines(processes)
            .Select(line => line.Trim().Split(' ', 4, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 4 && !fields[2].StartsWith('Z'))
            .Select(fields => (
                Kin: new Kin(int.Parse(fields[0], CultureInfo.InvariantCulture), int.Parse(fields[1], CultureInfo.InvariantCulture)),
                Marked: fields[3].Contains(marker, StringComparison.Ordinal)))
            .Where(candidate => candidate.Kin.Id != harness)
            .ToList();

        return ProcessFamilies.Of(
            [.. candidates.Select(candidate => candidate.Kin)],
            candidates.Where(candidate => candidate.Marked || roots.Contains(candidate.Kin.Id)).Select(candidate => candidate.Kin.Id));
    }

    public static IReadOnlyList<Listener> Listeners(Option<string> listing)
    {
        var listeners = new List<Listener>();
        var owner = 0;

        foreach (var line in Lines(listing))
        {
            if (line.StartsWith('p'))
            {
                owner = int.Parse(line[1..], CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith('n'))
            {
                listeners.Add(new Listener(int.Parse(line[(line.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture), owner));
            }
        }

        return [.. listeners.Distinct()];
    }

    private static string[] Lines(Option<string> output) =>
        output.Match(text => text, () => string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
