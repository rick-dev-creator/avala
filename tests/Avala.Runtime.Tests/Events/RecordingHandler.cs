using Avala.Sdk.Events;

namespace Avala.Runtime.Tests.Events;

internal sealed class RecordingHandler(int expected) : IHandle<Pinged>
{
    private readonly List<int> received = [];
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<int> Received => received;

    public Task Completion => completion.Task;

    public ValueTask HandleAsync(Pinged integrationEvent, CancellationToken cancellationToken)
    {
        received.Add(integrationEvent.Sequence);

        if (received.Count == expected)
        {
            completion.TrySetResult();
        }

        return ValueTask.CompletedTask;
    }
}
