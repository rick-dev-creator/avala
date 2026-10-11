using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using Avala.Runtime;
using Avala.Sdk;

namespace Avala.Host.Tests;

internal sealed class TelemetryGate : IPlugin
{
    public const string TestKey = "A-US-1234567890";

    private ImmutableList<string> sent = [];

    public PluginInfo Info { get; } = new("telemetry-gate", "Telemetry gate");

    public bool Configured { get; set; }

    public IReadOnlyList<string> Sent => Volatile.Read(ref sent);

    public IReadOnlyList<string> Events => [.. Sent.Select(body => JsonDocument.Parse(body).RootElement.GetProperty("eventName").GetString()!)];

    public void Register(IPluginRegistrar registrar)
    {
        if (Configured)
        {
            registrar.Services.AddTelemetrySender(new Endpoint(this), TestKey, Option<string>.None);
        }
    }

    private sealed class Endpoint(TelemetryGate gate) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            ImmutableInterlocked.Update(ref gate.sent, list => list.Add(body));

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
