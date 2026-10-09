using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal static class ConsoleHosts
{
    public static IReadOnlyList<TreeProcess> Attributed(IReadOnlyList<TreeProcess> members, Func<int, Option<int>> parentOf)
    {
        var hosted = members.Where(IsHost).ToLookup(host => parentOf(host.Id));

        return
        [
            .. members.Where(member => !IsHost(member)).Select(client => hosted[client.Id].Aggregate(
                client,
                (total, host) => total with { MemoryBytes = total.MemoryBytes + host.MemoryBytes, CpuTime = total.CpuTime + host.CpuTime })),
        ];
    }

    private static bool IsHost(TreeProcess member) => string.Equals(member.Name, "conhost", StringComparison.OrdinalIgnoreCase);
}
