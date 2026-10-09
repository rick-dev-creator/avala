using System.Collections.Immutable;
using System.Diagnostics;

namespace Avala.Sdk.Processes;

public readonly record struct ProcessTreeId(Guid Value)
{
    public const string Variable = "AVALA_PROCESS_TREE";

    public static ProcessTreeId New() => new(Guid.CreateVersion7());
}

public interface IProcessLauncher
{
    IReadOnlyDictionary<string, string> Environment { get; }

    Result<Process, ProcessError> Start(ProcessStartInfo info);
}

public sealed record TreeProcess(int Id, string Name, long MemoryBytes, TimeSpan CpuTime);

public interface IProcessTree : IProcessLauncher
{
    ProcessTreeId Id { get; }

    string Home { get; }

    ValueTask<IReadOnlyList<TreeProcess>> MembersAsync(CancellationToken cancellationToken);
}

public interface IProcessTrees
{
    IReadOnlyList<IProcessTree> Open { get; }

    ValueTask<IProcessTree> OpenAsync(string home, CancellationToken cancellationToken);

    Option<IProcessTree> In(string folder);

    Option<IProcessTree> Find(ProcessTreeId tree);

    ValueTask<IReadOnlyList<TreeProcess>> CloseAsync(ProcessTreeId tree, CancellationToken cancellationToken);
}

public interface IProcessEnvironment
{
    ValueTask<IReadOnlyDictionary<string, string>> ForAsync(string home, CancellationToken cancellationToken);
}

public sealed record Listener(int Port, Option<int> Process);

public interface IListeningPorts
{
    ValueTask<IReadOnlyList<Listener>> ListAsync(IReadOnlySet<int> owners, CancellationToken cancellationToken);
}

public sealed class UncontainedProcesses : IProcessLauncher
{
    private UncontainedProcesses()
    {
    }

    public static UncontainedProcesses Instance { get; } = new();

    public IReadOnlyDictionary<string, string> Environment { get; } = ImmutableDictionary<string, string>.Empty;

    public Result<Process, ProcessError> Start(ProcessStartInfo info) => ProcessStarts.Start(info);
}

public static class ProcessStarts
{
    public static Result<Process, ProcessError> Start(ProcessStartInfo info)
    {
        if (info.UseShellExecute)
        {
            return ProcessError.Invalid;
        }

        try
        {
            return Process.Start(info) is { } process ? process : ProcessError.Invalid;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ProcessError.NotFound;
        }
        catch (InvalidOperationException)
        {
            return ProcessError.Invalid;
        }
    }
}
