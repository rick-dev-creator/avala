using System.Globalization;
using System.Net;
using System.Text;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Receiving;
using Avala.Triggers.Records;
using Microsoft.Extensions.Logging;

namespace Avala.Triggers.Listening;

internal sealed partial class WebhookListener(TriggerCatalog catalog, WebhookDesk desk, IEnumerable<IPortLeases> leases, ILogger<WebhookListener> logger)
    : IWebhookEndpoint, IStartupTask, IAsyncDisposable
{
    public const string Holder = "avala-webhooks";

    public const int LargestBody = 64 * 1024;

    private const string Prefix = "/hooks/";

    private readonly SerialExecutor owner = new();
    private Option<(HttpListener Listener, Task Serving)> running;
    private WebhookEndpoint current = WebhookEndpoint.Off;
    private int disposed;

    public StartupStage Stage => StartupStage.Recovery;

    public WebhookEndpoint Current => Volatile.Read(ref current);

    public Task RunAsync(CancellationToken cancellationToken) => RefreshAsync(catalog.Current, cancellationToken);

    public Task RefreshAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async token =>
            {
                var wanted = snapshot.Triggers.Any(trigger => trigger.Webhook.IsSome);

                if (wanted && running.IsNone)
                {
                    await StartAsync(token);
                }
                else if (!wanted && running.IsSome)
                {
                    await StopAsync(token);
                }
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await owner.RunAsync(StopAsync, CancellationToken.None);
        await owner.DisposeAsync();
    }

    private async Task StartAsync(CancellationToken token)
    {
        var leased = Option<PortLease>.None;

        foreach (var lease in leases.Take(1))
        {
            leased = await lease.LeaseAsync(Holder, token);
        }

        await leased.Match(
            lease => ListenAsync(lease.First, token),
            () =>
            {
                Volatile.Write(ref current, new WebhookEndpoint(Option<string>.None, TriggerError.NoPortLease));

                return Task.CompletedTask;
            });
    }

    private async Task ListenAsync(int port, CancellationToken token)
    {
        var url = string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}{Prefix}");
        var listener = new HttpListener();
        listener.Prefixes.Add(url);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException failure)
        {
            LogFailed(url, failure.Message);
            listener.Close();
            await ReleaseAsync(token);
            Volatile.Write(ref current, new WebhookEndpoint(Option<string>.None, TriggerError.ListenerFailed));

            return;
        }

        running = (listener, Task.Run(() => ServeAsync(listener), CancellationToken.None));
        Volatile.Write(ref current, new WebhookEndpoint(url, Option<TriggerError>.None));
    }

    private async Task StopAsync(CancellationToken token)
    {
        await running.Match(
            async served =>
            {
                served.Listener.Close();
                await served.Serving;
                await ReleaseAsync(token);
            },
            () => Task.CompletedTask);
        running = Option<(HttpListener Listener, Task Serving)>.None;
        Volatile.Write(ref current, WebhookEndpoint.Off);
    }

    private async Task ReleaseAsync(CancellationToken token)
    {
        foreach (var lease in leases.Take(1))
        {
            await lease.ReleaseAsync(Holder, token);
        }
    }

    private async Task ServeAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception stopped) when (stopped is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            await AnswerAsync(context);
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        try
        {
            var (body, tooLarge) = await BodyAsync(context.Request);
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            var name = path.StartsWith(Prefix, StringComparison.Ordinal) ? path[Prefix.Length..] : string.Empty;
            var headers = context.Request.Headers.AllKeys.OfType<string>().ToDictionary(key => key, key => context.Request.Headers[key] ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var answer = await desk.ReceiveAsync(new WebhookRequest(context.Request.HttpMethod, Uri.UnescapeDataString(name), headers, body, tooLarge), CancellationToken.None);
            await RespondAsync(context.Response, answer);
        }
        catch (Exception failure) when (failure is HttpListenerException or IOException or ObjectDisposedException)
        {
            LogDropped(failure.Message);
        }
    }

    private static async Task<(byte[] Body, bool TooLarge)> BodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 > LargestBody)
        {
            return ([], true);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        while ((read = await request.InputStream.ReadAsync(chunk)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read));

            if (buffer.Length > LargestBody)
            {
                return ([], true);
            }
        }

        return (buffer.ToArray(), false);
    }

    private static async Task RespondAsync(HttpListenerResponse response, WebhookAnswer answer)
    {
        var text = answer.Run.Match(
            run => $$"""{"verdict":"{{answer.Verdict}}","run":"{{run:N}}"}""",
            () => $$"""{"verdict":"{{answer.Verdict}}"}""");
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = answer.Status;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;

        if (answer.RetryAfter > TimeSpan.Zero)
        {
            response.Headers["Retry-After"] = ((int)Math.Ceiling(answer.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhooks could not listen on {Url}: {Reason}")]
    private partial void LogFailed(string url, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "A webhook request was dropped: {Reason}")]
    private partial void LogDropped(string reason);
}
