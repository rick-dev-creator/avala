using System.Collections.Concurrent;
using System.Net;

namespace Avala.Testing;

public sealed record NotifierRequest(string Method, Uri Address, IReadOnlyDictionary<string, string> Headers, string Body);

public sealed class FakeNotifierEndpoint : HttpMessageHandler
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<NotifierRequest> received = new();
    private readonly ConcurrentQueue<HttpStatusCode> replies = new();
    private TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<NotifierRequest> Received => [.. received];

    public bool Unreachable { get; set; }

    public FakeNotifierEndpoint Replying(params HttpStatusCode[] statuses)
    {
        foreach (var status in statuses)
        {
            replies.Enqueue(status);
        }

        return this;
    }

    public async Task<IReadOnlyList<NotifierRequest>> ReceivedAsync(int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            var signal = Volatile.Read(ref arrived);

            if (received.Count >= count)
            {
                return Received;
            }

            await signal.Task.WaitAsync(Patience, cancellationToken);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);
        received.Enqueue(new NotifierRequest(request.Method.Method, request.RequestUri!, headers, body));
        Interlocked.Exchange(ref arrived, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        if (Unreachable)
        {
            throw new HttpRequestException("The notifier is unreachable.");
        }

        return new HttpResponseMessage(replies.TryDequeue(out var status) ? status : HttpStatusCode.OK);
    }
}
