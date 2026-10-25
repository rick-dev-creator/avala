using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Fleet.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class FleetGate : IPlugin, IDisposable
{
    public const string Server = "https://fleet.example";

    public const string Key = "avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public const string Settings = """{ "server": "https://fleet.example", "instance": "0199f6a0-0000-7000-8000-00000000000a", "instanceName": "desktop", "enabled": true, "keyRefused": false }""";

    public PluginInfo Info { get; } = new("fleet-gate", "Fleet gate");

    public FakeFleetEndpoint Endpoint { get; } = new();

    public FleetKeychain Keychain { get; } = new(Server, Key);

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddKeyedSingleton<HttpMessageHandler>(FleetTransport.Handler, Endpoint)
            .AddKeyedSingleton<IKeychain>(FleetTransport.Handler, Keychain);

    public void Dispose() => Endpoint.Dispose();
}

internal sealed class FleetKeychain(string account, string key) : IKeychain
{
    public ConcurrentDictionary<string, string> Keys { get; } = new(StringComparer.Ordinal) { [account] = key };

    public ValueTask<bool> AvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

    public ValueTask<Result<string, KeychainFailure>> StoreAsync(string account, string secret, CancellationToken cancellationToken)
    {
        Keys[account] = secret;

        return ValueTask.FromResult<Result<string, KeychainFailure>>(account);
    }

    public ValueTask<Result<Option<string>, KeychainFailure>> ReadAsync(string account, CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result<Option<string>, KeychainFailure>>(Keys.TryGetValue(account, out var found) ? found : Option<string>.None);

    public ValueTask<Result<string, KeychainFailure>> RemoveAsync(string account, CancellationToken cancellationToken)
    {
        Keys.TryRemove(account, out _);

        return ValueTask.FromResult<Result<string, KeychainFailure>>(account);
    }
}

internal sealed record FleetRequest(string Method, string PathAndQuery, string Authorization, string Body);

internal sealed class FakeFleetEndpoint : HttpMessageHandler
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(200);

    private readonly ConcurrentQueue<FleetRequest> received = new();

    private readonly ConcurrentQueue<Func<IReadOnlyList<JsonNode>, (HttpStatusCode Status, string Body)>> answers = new();

    private TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<FleetRequest> Received => [.. received];

    public IReadOnlyList<JsonNode> Events =>
        [.. Received.Where(request => request.PathAndQuery == "/activity/v1/events").SelectMany(request => JsonNode.Parse(request.Body)!["events"]!.AsArray()).Select(node => node!)];

    public string Usage { get; set; } = string.Empty;

    public string Jobs { get; set; } = """{ "jobs": [] }""";

    public void Answering(Func<IReadOnlyList<JsonNode>, (HttpStatusCode Status, string Body)> answer) => answers.Enqueue(answer);

    public async Task<IReadOnlyList<JsonNode>> EventsAsync(Func<IReadOnlyList<JsonNode>, bool> enough, CancellationToken cancellationToken)
    {
        while (true)
        {
            var signal = Volatile.Read(ref arrived);
            var events = Events;

            if (enough(events))
            {
                return events;
            }

            await signal.Task.WaitAsync(Patience, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<JsonNode>> EventsAsync(Func<IReadOnlyList<JsonNode>, bool> enough, Action nudge, CancellationToken cancellationToken)
    {
        for (var waited = TimeSpan.Zero; waited < Patience; waited += Step)
        {
            var signal = Volatile.Read(ref arrived);
            var events = Events;

            if (enough(events))
            {
                return events;
            }

            nudge();
            await signal.Task.WaitAsync(Step, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            cancellationToken.ThrowIfCancellationRequested();
        }

        throw new TimeoutException($"The fleet server received only {Events.Count} events.");
    }

    public static (HttpStatusCode Status, string Body) Accepting(IReadOnlyList<JsonNode> events) =>
        (HttpStatusCode.OK, Receipt([.. events.Select(Id)], [], []));

    public static string Receipt(IReadOnlyList<string> accepted, IReadOnlyList<(string Id, string Reason)> deferred, IReadOnlyList<(string Id, string Reason)> rejected) =>
        JsonSerializer.Serialize(new
        {
            accepted,
            duplicates = Array.Empty<string>(),
            deferred = deferred.Select(entry => new { id = entry.Id, reason = entry.Reason }),
            rejected = rejected.Select(entry => new { id = entry.Id, reason = entry.Reason }),
        });

    public static string Id(JsonNode node) => node["id"]!.GetValue<string>();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.PathAndQuery;
        var (status, answer) = path switch
        {
            "/instances/me" => (HttpStatusCode.OK, """{ "id": "0199f6a0-0000-7000-8000-00000000000a", "name": "desktop" }"""),
            _ when path.StartsWith("/activity/v1/usage", StringComparison.Ordinal) => (HttpStatusCode.OK, Usage),
            _ when path.StartsWith("/activity/v1/repositories/jobs", StringComparison.Ordinal) => (HttpStatusCode.OK, Jobs),
            "/activity/v1/events" => Answer(body),
            _ => (HttpStatusCode.NotFound, string.Empty),
        };
        received.Enqueue(new FleetRequest(request.Method.Method, path, request.Headers.Authorization?.ToString() ?? string.Empty, body));
        Interlocked.Exchange(ref arrived, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
    }

    private (HttpStatusCode Status, string Body) Answer(string body)
    {
        IReadOnlyList<JsonNode> events = [.. JsonNode.Parse(body)!["events"]!.AsArray().Select(node => node!)];

        return answers.TryDequeue(out var answer) ? answer(events) : Accepting(events);
    }
}
