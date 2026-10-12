using System.Text.RegularExpressions;

namespace Avala.Host.Tests;

internal static partial class RunLog
{
    private const int KeptEntries = 8;

    private const int LongestEntry = 1500;

    private static readonly string[] Problems = ["Warning", "Error", "Critical"];

    public static async Task<string> ProblemsAsync(string folder, CancellationToken cancellationToken)
    {
        var entries = new List<string>();

        foreach (var file in (Directory.Exists(folder) ? Directory.GetFiles(folder, "*.log") : []).Order(StringComparer.Ordinal))
        {
            entries.AddRange(Entries(await ReadAsync(file, cancellationToken)).Where(entry => Problems.Contains(EntryStart().Match(entry).Groups["level"].Value)));
        }

        return entries.Count == 0
            ? "; the run logged no warning or error"
            : $"; the run logged {string.Join(" | ", entries.TakeLast(KeptEntries).Select(entry => entry.Length > LongestEntry ? entry[..LongestEntry] : entry))}";
    }

    private static async Task<string> ReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> Entries(string log)
    {
        var starts = EntryStart().Matches(log).Select(match => match.Index).ToList();

        return starts.Select((start, index) => log[start..(index + 1 < starts.Count ? starts[index + 1] : log.Length)].TrimEnd());
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} (?<level>\w+) ", RegexOptions.Multiline)]
    private static partial Regex EntryStart();
}
