using System.Globalization;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal static class LinuxListings
{
    private const string Listening = "0A";

    public static IReadOnlyList<Listener> Listeners(IEnumerable<string> tables, IReadOnlyList<(int Owner, IReadOnlyList<long> Sockets)> held)
    {
        var sockets = new Dictionary<long, int>();

        foreach (var fields in tables.SelectMany(table => table.Split('\n').Skip(1)).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            if (fields.Length > 9 && fields[3] == Listening && long.TryParse(fields[9], CultureInfo.InvariantCulture, out var inode))
            {
                sockets[inode] = int.Parse(fields[1][(fields[1].LastIndexOf(':') + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
        }

        var holders = held
            .SelectMany(holder => holder.Sockets.Select(inode => (Inode: inode, holder.Owner)))
            .ToLookup(holding => holding.Inode, holding => holding.Owner);

        return
        [
            .. sockets
                .SelectMany(socket => holders.Contains(socket.Key)
                    ? holders[socket.Key].Distinct().Order().Select(owner => new Listener(socket.Value, owner))
                    : [new Listener(socket.Value, Option<int>.None)])
                .Distinct(),
        ];
    }
}
