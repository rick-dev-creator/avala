using System.Globalization;
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
    public async ValueTask<IReadOnlyList<Listener>> ListAsync(IReadOnlySet<int> owners, CancellationToken cancellationToken) =>
        (await Commands.OutputAsync("netstat", ["-ano", "-p", "TCP"], cancellationToken))
            .Match(output => output, () => string.Empty)
            .Split('\n')
            .Concat((await Commands.OutputAsync("netstat", ["-ano", "-p", "TCPv6"], cancellationToken)).Match(output => output, () => string.Empty).Split('\n'))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(fields => fields.Length >= 5 && fields[0] == "TCP" && fields[2].EndsWith(":0", StringComparison.Ordinal))
            .Select(fields => new Listener(Port(fields[1]), int.Parse(fields[^1], CultureInfo.InvariantCulture)))
            .Distinct()
            .ToList();

    private static int Port(string endpoint) => int.Parse(endpoint[(endpoint.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);
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
