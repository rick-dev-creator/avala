using System.Collections.Concurrent;
using Avala.Sdk;

namespace Avala.Testing;

public sealed class TestUiDispatcher : SynchronizationContext, IUiDispatcher, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly BlockingCollection<Action> work = [];
    private readonly List<(Func<bool> Condition, TaskCompletionSource Reached)> waiters = [];
    private readonly Thread thread;

    public TestUiDispatcher()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Test UI thread" };
        thread.Start();
    }

    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() =>
        {
            action();
            done.SetResult();
        }, cancellationToken);

        return new ValueTask(done.Task);
    }

    public async Task<T> ReadAsync<T>(Func<T> read)
    {
        var result = default(T)!;
        await InvokeAsync(() => result = read(), CancellationToken.None);

        return result;
    }

    public async Task UntilAsync(Func<bool> condition)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() => waiters.Add((condition, reached)));

        await reached.Task.WaitAsync(Patience);
    }

    public override void Post(SendOrPostCallback d, object? state) => work.Add(() => d(state));

    public void Dispose() => work.CompleteAdding();

    private void Run()
    {
        SetSynchronizationContext(this);

        foreach (var next in work.GetConsumingEnumerable())
        {
            next();

            foreach (var waiter in waiters.Where(waiter => waiter.Condition()).ToList())
            {
                waiters.Remove(waiter);
                waiter.Reached.SetResult();
            }
        }
    }
}
