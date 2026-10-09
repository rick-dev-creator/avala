using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal sealed class LinuxListeningPorts : IListeningPorts
{
    private const string Listening = "0A";

    public async ValueTask<IReadOnlyList<Listener>> ListAsync(IReadOnlySet<int> owners, CancellationToken cancellationToken)
    {
        var sockets = new Dictionary<long, int>();

        foreach (var table in (string[])["/proc/net/tcp", "/proc/net/tcp6"])
        {
            foreach (var fields in (await TableAsync(table, cancellationToken)).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            {
                if (fields.Length > 9 && fields[3] == Listening && long.TryParse(fields[9], CultureInfo.InvariantCulture, out var inode))
                {
                    sockets[inode] = int.Parse(fields[1][(fields[1].LastIndexOf(':') + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }
        }

        var holders = owners.SelectMany(owner => Sockets(owner).Select(inode => (Inode: inode, Owner: owner))).ToDictionary(held => held.Inode, held => held.Owner);

        return [.. sockets.Select(socket => new Listener(socket.Value, holders.TryGetValue(socket.Key, out var owner) ? owner : Option<int>.None)).Distinct()];
    }

    private static async Task<IReadOnlyList<string>> TableAsync(string table, CancellationToken cancellationToken)
    {
        try
        {
            return (await File.ReadAllTextAsync(table, cancellationToken)).Split('\n').Skip(1).ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static List<long> Sockets(int owner)
    {
        try
        {
            return Directory.EnumerateFiles($"/proc/{owner}/fd")
                .Select(link => new FileInfo(link).LinkTarget ?? string.Empty)
                .Where(target => target.StartsWith("socket:[", StringComparison.Ordinal))
                .Select(target => long.Parse(target[8..^1], CultureInfo.InvariantCulture))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

internal sealed class WindowsListeningPorts : IListeningPorts
{
    private const uint InsufficientBuffer = 122;
    private const int OwnerProcessListeners = 3;

    public ValueTask<IReadOnlyList<Listener>> ListAsync(IReadOnlySet<int> owners, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Listener>>([.. Read(TcpFamily.Ipv4).Concat(Read(TcpFamily.Ipv6)).Distinct()]);

    private static IReadOnlyList<Listener> Read(TcpFamily family)
    {
        var size = 0;
        byte[]? table = null;
        uint outcome;

        while ((outcome = GetExtendedTcpTable(table, ref size, order: false, family.AddressFamily, OwnerProcessListeners, 0)) == InsufficientBuffer)
        {
            table = new byte[size];
        }

        return outcome == 0 && table is not null ? TcpTables.Listeners(table, family) : [];
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        byte[]? table,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        int tableClass,
        uint reserved);
}

internal sealed record TcpFamily(int AddressFamily, int RowSize, int PortOffset, int ProcessOffset)
{
    public static TcpFamily Ipv4 { get; } = new(2, 24, 8, 20);

    public static TcpFamily Ipv6 { get; } = new(23, 56, 20, 52);
}

internal static class TcpTables
{
    private const int Header = 4;

    public static IReadOnlyList<Listener> Listeners(ReadOnlySpan<byte> table, TcpFamily family)
    {
        var listeners = new List<Listener>();
        var rows = Math.Min(BinaryPrimitives.ReadInt32LittleEndian(table), (table.Length - Header) / family.RowSize);

        for (var index = 0; index < rows; index++)
        {
            var row = table.Slice(Header + (index * family.RowSize), family.RowSize);
            listeners.Add(new Listener(BinaryPrimitives.ReadUInt16BigEndian(row[family.PortOffset..]), BinaryPrimitives.ReadInt32LittleEndian(row[family.ProcessOffset..])));
        }

        return listeners;
    }
}

internal sealed class MacListeningPorts : IListeningPorts
{
    public async ValueTask<IReadOnlyList<Listener>> ListAsync(IReadOnlySet<int> owners, CancellationToken cancellationToken)
    {
        var listeners = new List<Listener>();
        var owner = 0;

        foreach (var line in (await Commands.OutputAsync("lsof", ["-nP", "-iTCP", "-sTCP:LISTEN", "-Fpn"], cancellationToken)).Match(output => output, () => string.Empty).Split('\n'))
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
}
