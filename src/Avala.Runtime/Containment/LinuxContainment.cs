using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal sealed class LinuxContainment : IContainment
{
    public const int TicksPerSecond = 100;

    private readonly Option<string> setsid = FlatpakHost.IsSandboxed ? Option<string>.None : Executables.Find("setsid");

    public async ValueTask<IContainer> CreateAsync(ProcessTreeId tree, CancellationToken cancellationToken)
    {
        var uptime = await File.ReadAllTextAsync("/proc/uptime", cancellationToken);
        var seconds = double.Parse(uptime.Split(' ')[0], CultureInfo.InvariantCulture);

        return new LinuxContainer(tree, (long)((seconds - 1) * TicksPerSecond), setsid);
    }

    public async ValueTask<IReadOnlyList<IReadOnlyList<int>>> MemberIdsAsync(IReadOnlyList<IContainer> containers, CancellationToken cancellationToken)
    {
        if (containers.Count == 0)
        {
            return [];
        }

        var scan = await LinuxProcessScan.ReadAsync(cancellationToken);
        var members = new List<IReadOnlyList<int>>(containers.Count);

        foreach (var container in containers)
        {
            members.Add(container is LinuxContainer linux ? await linux.MemberIdsAsync(scan, cancellationToken) : await container.MemberIdsAsync(cancellationToken));
        }

        return members;
    }
}

internal sealed class LinuxContainer(ProcessTreeId tree, long since, Option<string> setsid) : IContainer
{
    private readonly string marker = $"{ProcessTreeId.Variable}={tree.Value}";
    private ImmutableHashSet<int> roots = [];

    public ProcessStartInfo Prepare(ProcessStartInfo info) =>
        setsid.Bind(wrapper => Executables.Resolve(info.FileName).Map(program => Wrapped(info, wrapper, program))).Match(wrapped => wrapped, () => info);

    public void Adopt(Process process) => ImmutableInterlocked.Update(ref roots, known => known.Add(process.Id));

    public async ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken) =>
        await MemberIdsAsync(await LinuxProcessScan.ReadAsync(cancellationToken), cancellationToken);

    public async ValueTask<IReadOnlyList<int>> MemberIdsAsync(LinuxProcessScan scan, CancellationToken cancellationToken)
    {
        var candidates = scan.Processes.Where(process => process.Start >= since).ToList();
        var known = Volatile.Read(ref roots);
        var members = new List<int>();

        foreach (var candidate in candidates)
        {
            if (known.Contains(candidate.Id) || known.Contains(candidate.Session)
                || await scan.TreeMarkerOfAsync(candidate.Id, cancellationToken) == Option<string>.Some(marker))
            {
                members.Add(candidate.Id);
            }
        }

        return ProcessFamilies.Of([.. candidates.Select(candidate => new Kin(candidate.Id, candidate.Parent))], members);
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
