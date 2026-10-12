using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using Avala.Preview.Pages;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Preview.Tests.Pages;

public sealed class PageProxyTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AServersPageIsServedThroughTheProxyWithAPolicyThatKeepsOtherOriginsOutAsync()
    {
        await using var server = await ServerAsync();
        var port = Workloads.FreePort();
        await using var proxy = Outcomes.Present(PageProxy.Start(port, server.Port));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/orders"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Running on port {server.Port}", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(proxy.Policy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task AllowingNetworkPassesTheServersPagesWithoutThePolicyAsync()
    {
        await using var server = await ServerAsync();
        var port = Workloads.FreePort();
        await using var proxy = Outcomes.Present(PageProxy.Start(port, server.Port));
        using var client = new HttpClient();

        proxy.AllowsNetwork = true;
        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task AWebSocketIsBridgedToTheServerAsync()
    {
        var upstreamPort = Workloads.FreePort();
        using var upstream = new HttpListener();
        upstream.Prefixes.Add($"http://localhost:{upstreamPort}/");
        upstream.Start();
        var echoing = EchoAsync(upstream);
        var port = Workloads.FreePort();
        await using var proxy = Outcomes.Present(PageProxy.Start(port, upstreamPort));
        using var socket = new ClientWebSocket();

        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/@vite/client"), Cancellation);
        await socket.SendAsync("update"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, Cancellation);
        var buffer = new byte[64];
        var received = await socket.ReceiveAsync(buffer, Cancellation);

        Assert.Equal("echo update", System.Text.Encoding.UTF8.GetString(buffer, 0, received.Count));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Cancellation);
        await echoing;
    }

    private static async Task EchoAsync(HttpListener listener)
    {
        var context = await listener.GetContextAsync();
        var accepted = await context.AcceptWebSocketAsync(subProtocol: null);
        using var socket = accepted.WebSocket;
        var buffer = new byte[64];
        var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
        await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes("echo " + System.Text.Encoding.UTF8.GetString(buffer, 0, received.Count)), WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await socket.ReceiveAsync(buffer, CancellationToken.None);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    private static async Task<Upstream> ServerAsync()
    {
        var port = Workloads.FreePort();
        var command = Workloads.Holding("serve");
        command.Environment["AVALA_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        var (process, _) = await Workloads.StartAsync(UncontainedProcesses.Instance, command, Cancellation);

        return new Upstream(process, port);
    }

    private sealed record Upstream(System.Diagnostics.Process Process, int Port) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Process.Kill();
            await Process.WaitForExitAsync(CancellationToken.None);
            Process.Dispose();
        }
    }
}
