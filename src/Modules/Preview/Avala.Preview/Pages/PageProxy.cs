using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using Avala.Sdk;

namespace Avala.Preview.Pages;

internal sealed class PageProxy : IAsyncDisposable
{
    private static readonly string[] HopHeaders =
    [
        "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection", "TE", "Trailer", "Host", "Content-Length",
        "Content-Security-Policy", "Content-Security-Policy-Report-Only",
    ];

    private readonly HttpListener listener;
    private readonly HttpClient client;
    private readonly Uri upstream;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task serving;
    private bool network;

    private PageProxy(HttpListener listener, int port, int target)
    {
        this.listener = listener;
        Port = port;
        Target = target;
        upstream = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{target}/"));
        client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        serving = ServeAsync();
    }

    public int Port { get; }

    public int Target { get; }

    public bool AllowsNetwork
    {
        get => Volatile.Read(ref network);
        set => Volatile.Write(ref network, value);
    }

    public string Policy =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"default-src 'self' http://localhost:{Target} http://127.0.0.1:{Target} 'unsafe-inline' 'unsafe-eval' data: blob:; connect-src 'self' ws://127.0.0.1:{Port} ws://localhost:{Target} http://localhost:{Target}");

    public static Option<PageProxy> Start(int port, int target)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/"));

        try
        {
            listener.Start();
        }
        catch (HttpListenerException)
        {
            listener.Close();

            return Option<PageProxy>.None;
        }

        return new PageProxy(listener, port, target);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Close();
        await serving;
        client.Dispose();
        stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        var answering = new List<Task>();

        while (listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                answering.RemoveAll(task => task.IsCompleted);
                answering.Add(AnswerAsync(context, stopping.Token));
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break;
            }
        }

        await Task.WhenAll(answering);
    }

    private async Task AnswerAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await (context.Request.IsWebSocketRequest ? BridgeAsync(context, cancellationToken) : ForwardAsync(context, cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or WebSocketException or HttpListenerException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            Fail(context.Response);
        }
    }

    private async Task ForwardAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        using var message = Outward(context.Request);
        using var answer = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var response = context.Response;
        response.StatusCode = (int)answer.StatusCode;
        Inward(answer, response);

        await using (var body = await answer.Content.ReadAsStreamAsync(cancellationToken))
        {
            await body.CopyToAsync(response.OutputStream, cancellationToken);
        }

        response.Close();
    }

    private HttpRequestMessage Outward(HttpListenerRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.HttpMethod), new Uri(upstream, request.RawUrl));

        if (request.HasEntityBody)
        {
            message.Content = new StreamContent(request.InputStream);
        }

        foreach (var name in request.Headers.AllKeys.OfType<string>().Where(name => !HopHeaders.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            var values = request.Headers.GetValues(name) ?? [];

            if (!message.Headers.TryAddWithoutValidation(name, values))
            {
                message.Content?.Headers.TryAddWithoutValidation(name, values);
            }
        }

        return message;
    }

    private void Inward(HttpResponseMessage answer, HttpListenerResponse response)
    {
        foreach (var (name, values) in answer.Headers.Concat(answer.Content.Headers).Where(header => !HopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase)))
        {
            foreach (var value in values)
            {
                response.AddHeader(name, name.Equals("Location", StringComparison.OrdinalIgnoreCase) ? Local(value) : value);
            }
        }

        if (!AllowsNetwork)
        {
            response.AddHeader("Content-Security-Policy", Policy);
        }

        if (answer.Content.Headers.ContentLength is { } length)
        {
            response.ContentLength64 = length;
        }
        else
        {
            response.SendChunked = true;
        }
    }

    private async Task BridgeAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var protocols = (context.Request.Headers["Sec-WebSocket-Protocol"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var outward = new ClientWebSocket();

        foreach (var protocol in protocols)
        {
            outward.Options.AddSubProtocol(protocol);
        }

        await outward.ConnectAsync(new Uri(new Uri(string.Create(CultureInfo.InvariantCulture, $"ws://localhost:{Target}/")), context.Request.RawUrl), cancellationToken);
        var accepted = await context.AcceptWebSocketAsync(outward.SubProtocol);
        using var inward = accepted.WebSocket;
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await Task.WhenAll(PumpAsync(inward, outward, ending), PumpAsync(outward, inward, ending));
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationTokenSource ending)
    {
        var buffer = new byte[16 * 1024];

        try
        {
            while (from.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var received = await from.ReceiveAsync(buffer, ending.Token);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync(from, to, ending.Token);

                    return;
                }

                await to.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, received.EndOfMessage, ending.Token);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            await ending.CancelAsync();
        }
    }

    private static async Task CloseAsync(WebSocket from, WebSocket to, CancellationToken cancellationToken)
    {
        if (to.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await to.CloseOutputAsync(from.CloseStatus ?? WebSocketCloseStatus.NormalClosure, from.CloseStatusDescription, cancellationToken);
        }
    }

    private string Local(string location) =>
        Uri.TryCreate(location, UriKind.Absolute, out var absolute) && absolute.IsLoopback && absolute.Port == Target
            ? new UriBuilder(absolute) { Host = "127.0.0.1", Port = Port }.Uri.ToString()
            : location;

    private static void Fail(HttpListenerResponse response)
    {
        try
        {
            response.StatusCode = (int)HttpStatusCode.BadGateway;
            response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }
}
