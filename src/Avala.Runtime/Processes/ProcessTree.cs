using System.Diagnostics;
using Avala.Runtime.Containment;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class ProcessTree(ProcessTreeId id, string home, IReadOnlyDictionary<string, string> environment, IContainer container) : IProcessTree
{
    private const int KillRounds = 20;

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

    public async ValueTask<IReadOnlyList<TreeProcess>> MembersAsync(CancellationToken cancellationToken) =>
        [.. (await container.MemberIdsAsync(cancellationToken)).Select(Describe).SelectMany(described => described.Match<TreeProcess[]>(found => [found], () => []))];

    public async Task<IReadOnlyList<TreeProcess>> KillAsync(TimeProvider clock, CancellationToken cancellationToken)
    {
        for (var round = 0; round < KillRounds; round++)
        {
            var members = await container.MemberIdsAsync(cancellationToken);

            if (members.Count == 0)
            {
                return [];
            }

            foreach (var member in members)
            {
                Kill(member);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), clock, cancellationToken);
        }

        return await MembersAsync(cancellationToken);
    }

    public void Release() => container.Dispose();

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

    private static void Kill(int member)
    {
        try
        {
            using var process = Process.GetProcessById(member);
            process.Kill();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }
}
