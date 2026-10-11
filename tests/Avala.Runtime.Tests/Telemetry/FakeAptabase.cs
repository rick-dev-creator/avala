using System.Collections.Immutable;
using System.Net;

namespace Avala.Runtime.Tests.Telemetry;

internal sealed record SentEvent(Uri Address, string Key, string Body);

internal sealed class FakeAptabase : HttpMessageHandler
{
    private ImmutableList<SentEvent> sent = [];

    public bool Failing { get; set; }

    public IReadOnlyList<SentEvent> Sent => Volatile.Read(ref sent);

    public IReadOnlyList<string> Events => [.. Sent.Select(item => System.Text.Json.JsonDocument.Parse(item.Body).RootElement.GetProperty("eventName").GetString()!)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Failing)
        {
            throw new HttpRequestException("No route to host.");
        }

        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        ImmutableInterlocked.Update(ref sent, list => list.Add(new SentEvent(request.RequestUri!, string.Join(",", request.Headers.GetValues("App-Key")), body)));

        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
