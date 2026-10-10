using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Listening;
using Avala.Triggers.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Triggers.Tests.Listening;

public sealed class WebhookListenerTests
{
    private const string Secret = "s3cret";

    private const string Body = """{ "issue": { "title": "Totals round wrong" } }""";

    private static readonly string Hook =
        $$"""{ "id": "issue", "repository": "{{Triggered.Repository}}", "instruction": "Triage", "webhook": { "secretEnv": "HOOK_SECRET", "ratePerHour": 1 } }""";

    [Fact]
    public async Task TheEndpointListensOnItsLeasedPortWhileATriggerDeclaresAWebhookAndAnswersItsDeliveriesAsync()
    {
        await using var triggered = await StartAsync();
        var leases = new FakeLeases(FreePort());
        await using var listener = Listener(triggered, leases);

        await listener.RunAsync(Triggered.Cancellation);
        var url = listener.Current.Url.Match(found => found, () => string.Empty);
        using var client = new HttpClient();
        using var accepted = await client.SendAsync(Signed(triggered, $"{url}issue", "n-1"), Triggered.Cancellation);
        using var limited = await client.SendAsync(Signed(triggered, $"{url}issue", "n-2"), Triggered.Cancellation);
        var run = await triggered.FiredAsync(_ => true);
        await listener.RefreshAsync(CatalogSnapshot.Empty, Triggered.Cancellation);

        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{leases.Port}/hooks/"), url);
        Assert.Equal((HttpStatusCode.Accepted, $$"""{"verdict":"Accepted","run":"{{run.Id:N}}"}"""), (accepted.StatusCode, await accepted.Content.ReadAsStringAsync(Triggered.Cancellation)));
        Assert.Equal((HttpStatusCode.TooManyRequests, "3600"), (limited.StatusCode, Assert.Single(limited.Headers.GetValues("Retry-After"))));
        Assert.Equal(WebhookEndpoint.Off, listener.Current);
        Assert.Equal([WebhookListener.Holder], leases.Released);
    }

    [Fact]
    public async Task ABodyOverTheLimitIsRefusedWhetherItsLengthIsDeclaredOrStreamedAsync()
    {
        await using var triggered = await StartAsync();
        await using var listener = Listener(triggered, new FakeLeases(FreePort()));
        await listener.RunAsync(Triggered.Cancellation);
        var url = listener.Current.Url.Match(found => found, () => string.Empty);
        var large = new byte[WebhookListener.LargestBody + 1];
        using var client = new HttpClient();

        using var declared = await client.PostAsync($"{url}issue", new ByteArrayContent(large), Triggered.Cancellation);
        using var chunked = new HttpRequestMessage(HttpMethod.Post, $"{url}issue") { Content = new ByteArrayContent(large) };
        chunked.Headers.TransferEncodingChunked = true;
        using var streamed = await client.SendAsync(chunked, Triggered.Cancellation);

        Assert.Equal([HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.RequestEntityTooLarge], new[] { declared.StatusCode, streamed.StatusCode });
        Assert.Empty(triggered.Jobs.Requests);
    }

    [Fact]
    public async Task WithoutAPortLeaseOrWithItsPortTakenTheEndpointReportsWhyItIsNotListeningAsync()
    {
        await using var triggered = await StartAsync();
        var port = FreePort();
        await using var unleased = Listener(triggered, new FakeLeases(Option<int>.None));
        await using var first = Listener(triggered, new FakeLeases(port));
        var taken = new FakeLeases(port);
        await using var second = Listener(triggered, taken);

        await unleased.RunAsync(Triggered.Cancellation);
        await first.RunAsync(Triggered.Cancellation);
        await second.RunAsync(Triggered.Cancellation);

        Assert.Equal(new WebhookEndpoint(Option<string>.None, TriggerError.NoPortLease), unleased.Current);
        Assert.Equal(new WebhookEndpoint(Option<string>.None, TriggerError.ListenerFailed), second.Current);
        Assert.Equal([WebhookListener.Holder], taken.Released);
    }

    private static Task<Triggered> StartAsync() =>
        Triggered.StartAsync(Declared.Machine(Hook), world => world.Secrets.Values["HOOK_SECRET"] = Secret);

    private static WebhookListener Listener(Triggered triggered, FakeLeases leases) =>
        new(triggered.Catalog, triggered.Desk, [leases], NullLogger<WebhookListener>.Instance);

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static HttpRequestMessage Signed(Triggered triggered, string url, string nonce)
    {
        var now = triggered.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var bytes = Encoding.UTF8.GetBytes(Body);
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
        request.Headers.Add(WebhookSignature.TimestampHeader, now);
        request.Headers.Add(WebhookSignature.NonceHeader, nonce);
        request.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Of(Secret, now, nonce, bytes));

        return request;
    }

    private sealed class FakeLeases(Option<int> port) : IPortLeases
    {
        private readonly List<string> released = [];

        public int Port => port.Match(found => found, () => 0);

        public IReadOnlyList<string> Released => [.. released];

        public ValueTask<Option<PortLease>> LeaseAsync(string holder, CancellationToken cancellationToken) =>
            ValueTask.FromResult(port.Map(found => new PortLease(holder, found, found)));

        public Task ReleaseAsync(string holder, CancellationToken cancellationToken)
        {
            released.Add(holder);

            return Task.CompletedTask;
        }
    }
}
