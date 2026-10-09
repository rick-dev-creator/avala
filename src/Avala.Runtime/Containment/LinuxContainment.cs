using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal sealed class LinuxContainment : IContainment
{
    public const int TicksPerSecond = 100;

    private readonly Option<string> setsid = Executables.Find("setsid");

    public async ValueTask<IContainer> CreateAsync(ProcessTreeId tree, CancellationToken cancellationToken)
    {
        var uptime = await File.ReadAllTextAsync("/proc/uptime", cancellationToken);
        var seconds = double.Parse(uptime.Split(' ')[0], CultureInfo.InvariantCulture);

        return new LinuxContainer(tree, (long)((seconds - 1) * TicksPerSecond), setsid);
    }
}

internal sealed class LinuxContainer(ProcessTreeId tree, long since, Option<string> setsid) : IContainer
{
    private readonly string marker = $"{ProcessTreeId.Variable}={tree.Value}";
    private ImmutableHashSet<int> roots = [];

    public ProcessStartInfo Prepare(ProcessStartInfo info) =>
        setsid.Bind(wrapper => Executables.Resolve(info.FileName).Map(program => Wrapped(info, wrapper, program))).Match(wrapped => wrapped, () => info);

    public void Adopt(Process process) => ImmutableInterlocked.Update(ref roots, known => known.Add(process.Id));

    public async ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<Stat>();

        foreach (var folder in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id != Environment.ProcessId
                && (await StatAsync(id, cancellationToken)).TryGetValue(out var stat, out _)
                && stat is { Zombie: false } && stat.Start >= since)
            {
                candidates.Add(stat);
            }
        }

        var known = Volatile.Read(ref roots);
        var members = new HashSet<int>();

        foreach (var candidate in candidates)
        {
            if (known.Contains(candidate.Id) || known.Contains(candidate.Session) || await MarkedAsync(candidate.Id, cancellationToken))
            {
                members.Add(candidate.Id);
            }
        }

        while (candidates.Where(candidate => !members.Contains(candidate.Id) && members.Contains(candidate.Parent)).ToList() is { Count: > 0 } descendants)
        {
            members.UnionWith(descendants.Select(descendant => descendant.Id));
        }

        return [.. members];
    }

    public void Dispose()
    {
    }

    private static ProcessStartInfo Wrapped(ProcessStartInfo info, string wrapper, string program)
    {
        info.FileName = wrapper;

        if (info.ArgumentList.Count > 0 || info.Arguments.Length == 0)
        {
            info.ArgumentList.Insert(0, program);
        }
        else
        {
            info.Arguments = $"\"{program}\" {info.Arguments}";
        }

        return info;
    }

    private static async Task<Result<Stat, ProcessError>> StatAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync($"/proc/{id}/stat", cancellationToken);
            var fields = text[(text.LastIndexOf(')') + 2)..].Split(' ');

            return new Stat(
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

    private async Task<bool> MarkedAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var environment = Encoding.UTF8.GetString(await File.ReadAllBytesAsync($"/proc/{id}/environ", cancellationToken));

            return environment.Split('\0').Contains(marker, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record Stat(int Id, bool Zombie, int Parent, int Session, long Start);
}

internal static class Executables
{
    public static Option<string> Find(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists)
            .ToOption();

    public static Option<string> Resolve(string program) =>
        program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? File.Exists(program) ? Path.GetFullPath(program) : Option<string>.None
            : Find(program);
}
