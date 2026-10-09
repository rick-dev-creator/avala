using Avala.Sdk.Events;

namespace Avala.Runtime.Tests.Events;

internal sealed class TwoEventHandler(int expected) : IHandle<Pinged>, IHandle<Ponged>
{
    private readonly List<string> received = [];
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Received => received;

    public Task Completion => completion.Task;

    public async ValueTask HandleAsync(Pinged integrationEvent, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Record($"ping {integrationEvent.Sequence}");
    }

    public ValueTask HandleAsync(Ponged integrationEvent, CancellationToken cancellationToken)
    {
        Record($"pong {integrationEvent.Sequence}");

        return ValueTask.CompletedTask;
    }

    private void Record(string entry)
    {
        received.Add(entry);

        if (received.Count == expected)
        {
            completion.TrySetResult();
        }
    }
}
