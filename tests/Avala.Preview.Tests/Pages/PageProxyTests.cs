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
        await using var proxy = Outcomes.Present(Workloads.OnFreePort(port => PageProxy.Start(port, server.Port)));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{proxy.Port}/orders"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Running on port {server.Port}", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(proxy.Policy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task AllowingNetworkPassesTheServersPagesWithoutThePolicyAsync()
    {
        await using var server = await ServerAsync();
        await using var proxy = Outcomes.Present(Workloads.OnFreePort(port => PageProxy.Start(port, server.Port)));
        using var client = new HttpClient();

        proxy.AllowsNetwork = true;
        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{proxy.Port}/"), Cancellation);

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
        await using var proxy = Outcomes.Present(Workloads.OnFreePort(port => PageProxy.Start(port, upstreamPort)));
        using var socket = new ClientWebSocket();

        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{proxy.Port}/@vite/client"), Cancellation);
        await socket.SendAsync("update"u8.ToArray(), WebSocketMessageType.Text, endOfMessage: true, Cancellation);
        var buffer = new byte[64];
        var received = await socket.ReceiveAsync(buffer, Cancellation);

        Assert.Equal("echo update", System.Text.Encoding.UTF8.GetString(buffer, 0, received.Count));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Cancellation);
        await echoing;
    }

    [Fact]
    public async Task ARedirectToTheServerItselfStaysOnTheProxyAsync()
    {
        var upstreamPort = Workloads.FreePort();
        using var upstream = new HttpListener();
        upstream.Prefixes.Add($"http://localhost:{upstreamPort}/");
        upstream.Start();
        var redirecting = RedirectAsync(upstream, $"http://localhost:{upstreamPort}/signed-in?next=orders");
        await using var proxy = Outcomes.Present(Workloads.OnFreePort(port => PageProxy.Start(port, upstreamPort)));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{proxy.Port}/sign-in"), Cancellation);
        await redirecting;

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new Uri($"http://127.0.0.1:{proxy.Port}/signed-in?next=orders"), response.Headers.Location);
    }

    [Fact]
    public async Task AFormPostedThroughTheProxyReachesTheServerWithItsBodyAndTypeAsync()
    {
        var upstreamPort = Workloads.FreePort();
        using var upstream = new HttpListener();
        upstream.Prefixes.Add($"http://localhost:{upstreamPort}/");
        upstream.Start();
        var receiving = ReceiveAsync(upstream);
        await using var proxy = Outcomes.Present(Workloads.OnFreePort(port => PageProxy.Start(port, upstreamPort)));
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent([new("order", "1042")]);

        var response = await client.PostAsync(new Uri($"http://127.0.0.1:{proxy.Port}/orders"), form, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("POST", "application/x-www-form-urlencoded", "order=1042"), await receiving);
    }

    private static async Task<(string Method, string? Type, string Body)> ReceiveAsync(HttpListener listener)
    {
        var context = await listener.GetContextAsync();
        using var reader = new StreamReader(context.Request.InputStream);
        var received = (context.Request.HttpMethod, context.Request.ContentType, await reader.ReadToEndAsync(CancellationToken.None));
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.Close();

        return received;
    }

    private static async Task RedirectAsync(HttpListener listener, string location)
    {
        var context = await listener.GetContextAsync();
        context.Response.StatusCode = (int)HttpStatusCode.Redirect;
        context.Response.RedirectLocation = location;
        context.Response.Close();
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
