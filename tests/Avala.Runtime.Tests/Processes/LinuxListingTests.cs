using Avala.Runtime.Containment;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Tests.Processes;

public sealed class LinuxListingTests
{
    private const string Tcp = """
          sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
           0: 0100007F:5DC0 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 52614851 1 0000000000000000 100 0 0 10 0
           1: 0100007F:1435 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 52614900 1 0000000000000000 100 0 0 10 0
           2: 0100007F:A1B2 0100007F:5DC0 01 00000000:00000000 00:00000000 00000000  1000        0 52614999 1 0000000000000000 20 4 30 10 -1
        """;

    private const string Tcp6 = """
          sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
           0: 00000000000000000000000000000000:5DC0 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 52614852 1 0000000000000000 100 0 0 10 0
        """;

    [Fact]
    public void ASocketHeldByEveryProcessThatInheritedItListsThePortForEachOfThem() =>
        Assert.Equal(
            [new Listener(24_000, 812), new Listener(24_000, 913), new Listener(5_173, Option<int>.None)],
            LinuxListings.Listeners([Tcp, Tcp6], [(913, [52614851, 52614852]), (812, [52614851])]));
}
