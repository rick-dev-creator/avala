using System.Diagnostics;
using System.Runtime.InteropServices;
using Avala.Runtime.Containment;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class ProcessTree(ProcessTreeId id, string home, IReadOnlyDictionary<string, string> environment, IContainer container) : IProcessTree
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private const int KillRounds = 20;

    private const int Terminate = 15;

    private static readonly TimeSpan Settling = TimeSpan.FromMilliseconds(250);

    public ProcessTreeId Id { get; } = id;

    public string Home { get; } = home;

    public IReadOnlyDictionary<string, string> Environment { get; } = environment;

    public Result<Process, ProcessError> Start(ProcessStartInfo info)
    {
        if (info.UseShellExecute)
        {
            return ProcessError.Invalid;
        }

        foreach (var (name, value) in Environment)
        {
            info.Environment[name] = value;
        }

        return ProcessStarts.Start(container.Prepare(info)).Map(process =>
        {
            container.Adopt(process);

            return process;
        });
    }

    public async ValueTask<IReadOnlyList<TreeProcess>> MembersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TreeProcess> members =
            [.. (await container.MemberIdsAsync(cancellationToken)).Select(Describe).SelectMany(described => described.Match<TreeProcess[]>(found => [found], () => []))];

        return OperatingSystem.IsWindows() ? ConsoleHosts.Attributed(members, WindowsProcesses.ParentOf) : members;
    }

    public async Task<IReadOnlyList<TreeProcess>> KillAsync(TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            await AskToEndAsync(clock, cancellationToken);
        }

        for (var round = 0; round < KillRounds; round++)
        {
            var members = await container.MemberIdsAsync(cancellationToken);

            if (members.Count == 0)
            {
                return [];
            }

            var killed = Ended(members, Kill);

            try
            {
                await ExitedAsync(killed, cancellationToken).WaitAsync(Settling, clock, cancellationToken);
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                killed.ForEach(process => process.Dispose());
            }
        }

        return await MembersAsync(cancellationToken);
    }

    public void Release() => container.Dispose();

    private async Task AskToEndAsync(TimeProvider clock, CancellationToken cancellationToken)
    {
        var members = await container.MemberIdsAsync(cancellationToken);

        if (members.Count == 0)
        {
            return;
        }

        var grace = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var timer = clock.CreateTimer(_ => grace.TrySetResult(), null, Grace, Timeout.InfiniteTimeSpan);
        var asked = Ended(members, process => Signal(process.Id, Terminate) == 0);

        try
        {
            await Task.WhenAny(ExitedAsync(asked, cancellationToken), grace.Task).WaitAsync(cancellationToken);
        }
        finally
        {
            asked.ForEach(process => process.Dispose());
        }
    }

    private static Task ExitedAsync(List<Process> processes, CancellationToken cancellationToken) =>
        Task.WhenAll(processes.Select(process => process.WaitForExitAsync(cancellationToken)));

    private static List<Process> Ended(IEnumerable<int> members, Func<Process, bool> end) =>
        [.. members.Select(member => EndMember(member, end)).SelectMany(found => found.Match<Process[]>(process => [process], () => []))];

    private static bool Kill(Process process)
    {
        process.Kill();

        return true;
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Signal(int process, int signal);

    private static Option<TreeProcess> Describe(int member)
    {
        try
        {
            using var process = Process.GetProcessById(member);

            return new TreeProcess(member, process.ProcessName, process.WorkingSet64, process.TotalProcessorTime);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Option<TreeProcess>.None;
        }
    }

    private static Option<Process> EndMember(int member, Func<Process, bool> end)
    {
        Process? process = null;

        try
        {
            process = Process.GetProcessById(member);

            if (end(process))
            {
                return process;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }

        process?.Dispose();

        return Option<Process>.None;
    }
}
