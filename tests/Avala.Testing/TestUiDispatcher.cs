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
        work.Add(
            () =>
            {
                try
                {
                    action();
                    done.SetResult();
                }
                catch (Exception failure)
                {
                    done.SetException(failure);
                }
            },
            cancellationToken);

        return new ValueTask(done.Task.WaitAsync(Patience, cancellationToken));
    }

    public async Task<T> ReadAsync<T>(Func<T> read)
    {
        var result = default(T)!;
        await InvokeAsync(() => result = read(), CancellationToken.None);

        return result;
    }

    public Task UntilAsync(Func<bool> condition) => UntilAsync(condition, () => "no description of the state was given");

    public async Task UntilAsync(Func<bool> condition, Func<string> describe)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() => waiters.Add((condition, reached)));

        try
        {
            await reached.Task.WaitAsync(Patience);
        }
        catch (TimeoutException)
        {
            var state = await ReadAsync(() => Describe(describe));

            throw new TimeoutException($"The condition was not reached within {Patience.TotalSeconds:0} s. Last state: {state}");
        }
    }

    private static string Describe(Func<string> describe)
    {
        try
        {
            return describe();
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            return $"the state could not be read: {failure.GetType().Name}: {failure.Message}";
        }
    }

    public override void Post(SendOrPostCallback d, object? state) => work.Add(() => d(state));

    public void Dispose() => work.CompleteAdding();

    private void Run()
    {
        SetSynchronizationContext(this);

        foreach (var next in work.GetConsumingEnumerable())
        {
            try
            {
                next();
            }
            catch (Exception failure)
            {
                Fail(failure);
            }

            Check();
        }
    }

    private void Check()
    {
        foreach (var waiter in waiters.ToList())
        {
            try
            {
                if (waiter.Condition())
                {
                    waiters.Remove(waiter);
                    waiter.Reached.SetResult();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void Fail(Exception failure)
    {
        foreach (var waiter in waiters)
        {
            waiter.Reached.TrySetException(failure);
        }

        waiters.Clear();
    }
}
