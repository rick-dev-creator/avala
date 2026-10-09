using System.Buffers.Binary;
using Avala.Runtime.Containment;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Tests.Processes;

public sealed class TcpTableTests
{
    [Fact]
    public void EveryRowOfAnIpv4ListenerTableIsAPortInNetworkOrderHeldByItsProcess()
    {
        var table = Table(24, (8, 20, 24_000, 4_321), (8, 20, 443, 4));

        Assert.Equal([new Listener(24_000, 4_321), new Listener(443, 4)], TcpTables.Listeners(table, TcpFamily.Ipv4));
    }

    [Fact]
    public void EveryRowOfAnIpv6ListenerTableIsAPortInNetworkOrderHeldByItsProcess()
    {
        var table = Table(56, (20, 52, 24_009, 987), (20, 52, 5_000, 12));

        Assert.Equal([new Listener(24_009, 987), new Listener(5_000, 12)], TcpTables.Listeners(table, TcpFamily.Ipv6));
    }

    private static byte[] Table(int rowSize, params (int PortOffset, int ProcessOffset, ushort Port, int Process)[] rows)
    {
        var table = new byte[4 + (rowSize * rows.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(table, rows.Length);

        foreach (var (row, index) in rows.Select((row, index) => (row, index)))
        {
            var start = 4 + (rowSize * index);
            table.AsSpan(start, rowSize).Fill(0xAB);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(start + row.PortOffset), row.Port);
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(start + row.ProcessOffset), row.Process);
        }

        return table;
    }
}
