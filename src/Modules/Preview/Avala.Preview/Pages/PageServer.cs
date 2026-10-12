using System.Globalization;
using System.Net;
using Avala.Preview.Navigation;
using Avala.Preview.Origins;
using Avala.Sdk;

namespace Avala.Preview.Pages;

internal sealed class PageServer : IAsyncDisposable
{
    public const string Policy = "default-src 'self' 'unsafe-inline' 'unsafe-eval' data: blob:; connect-src 'self' ws://127.0.0.1:*";

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".wasm"] = "application/wasm",
        [".glb"] = "model/gltf-binary",
        [".gltf"] = "model/gltf+json",
        [".txt"] = "text/plain; charset=utf-8",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
    };

    private readonly HttpListener listener;
    private readonly string root;
    private readonly Task serving;
    private bool network;

    private PageServer(HttpListener listener, string root, int port)
    {
        this.listener = listener;
        this.root = root;
        Port = port;
        serving = ServeAsync();
    }

    public int Port { get; }

    public bool AllowsNetwork
    {
        get => Volatile.Read(ref network);
        set => Volatile.Write(ref network, value);
    }

    public static Option<PageServer> Start(string worktree, int port)
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

            return Option<PageServer>.None;
        }

        return new PageServer(listener, RealFiles.Folder(worktree), port);
    }

    public async ValueTask DisposeAsync()
    {
        listener.Close();
        await serving;
    }

    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            await AnswerAsync(context);
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        var response = context.Response;

        try
        {
            response.Headers["Cache-Control"] = "no-store";

            if (!AllowsNetwork)
            {
                response.Headers["Content-Security-Policy"] = Policy;
            }

            if (context.Request.HttpMethod is not ("GET" or "HEAD"))
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;

                return;
            }

            await Find(context.Request.Url).Match(
                file => SendAsync(response, file, context.Request.HttpMethod == "HEAD"),
                () =>
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;

                    return Task.CompletedTask;
                });
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private Option<string> Find(Uri? address)
    {
        var relative = Uri.UnescapeDataString(address?.AbsolutePath ?? "/").TrimStart('/');
        var asked = Path.GetFullPath(Path.Combine(root, relative.Length == 0 ? "index.html" : relative));
        var real = Directory.Exists(asked) ? Path.Combine(asked, "index.html").Canonical() : asked.Canonical();

        return PreviewOrigins.IsInside(root, real) && File.Exists(real) ? real : Option<string>.None;
    }

    private static async Task SendAsync(HttpListenerResponse response, string file, bool headOnly)
    {
        response.ContentType = Types.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous);
        response.ContentLength64 = stream.Length;

        if (!headOnly)
        {
            await stream.CopyToAsync(response.OutputStream);
        }
    }
}
