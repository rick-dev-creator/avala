using System.Globalization;
using System.Text;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal sealed class LinuxProcessScan
{
    private const string TreePrefix = $"{ProcessTreeId.Variable}=";

    private readonly Dictionary<int, Option<string>> trees = [];

    private LinuxProcessScan(IReadOnlyList<LinuxProcess> processes) => Processes = processes;

    public IReadOnlyList<LinuxProcess> Processes { get; }

    public static async Task<LinuxProcessScan> ReadAsync(CancellationToken cancellationToken)
    {
        var processes = new List<LinuxProcess>();

        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id != Environment.ProcessId
                && (await StatAsync(id, cancellationToken)).TryGetValue(out var process, out _)
                && process is { Zombie: false })
            {
                processes.Add(process);
            }
        }

        return new LinuxProcessScan(processes);
    }

    public async ValueTask<Option<string>> TreeMarkerOfAsync(int id, CancellationToken cancellationToken)
    {
        if (!trees.TryGetValue(id, out var marker))
        {
            marker = await ReadTreeMarkerAsync(id, cancellationToken);
            trees[id] = marker;
        }

        return marker;
    }

    private static async Task<Result<LinuxProcess, ProcessError>> StatAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync($"/proc/{id}/stat", cancellationToken);
            var fields = text[(text.LastIndexOf(')') + 2)..].Split(' ');

            return new LinuxProcess(
                id,
                fields[0] == "Z",
                int.Parse(fields[1], CultureInfo.InvariantCulture),
                int.Parse(fields[3], CultureInfo.InvariantCulture),
                long.Parse(fields[19], CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException)
        {
            return ProcessError.NotFound;
        }
    }

    private static async Task<Option<string>> ReadTreeMarkerAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var environment = Encoding.UTF8.GetString(await File.ReadAllBytesAsync($"/proc/{id}/environ", cancellationToken));

            return environment.Split('\0').FirstOrDefault(variable => variable.StartsWith(TreePrefix, StringComparison.Ordinal)).ToOption();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Option<string>.None;
        }
    }
}

internal sealed record LinuxProcess(int Id, bool Zombie, int Parent, int Session, long Start);
