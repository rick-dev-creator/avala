using System.Collections.Concurrent;
using Avala.Sdk;
using Avala.Sdk.Presentation;

namespace Avala.Testing;

public sealed class TestUiDispatcher : SynchronizationContext, IUiDispatcher, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly BlockingCollection<Action> work = [];
    private readonly List<(Func<bool> Condition, TaskCompletionSource Reached)> waiters = [];

    public TestUiDispatcher()
    {
        new Thread(Run) { IsBackground = true, Name = "Test UI thread" }.Start();
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

    public async Task<T> RunAsync<T>(Func<Task<T>> command) => await await ReadAsync(command);

    public async Task RunAsync(Func<Task> command) => await await ReadAsync(command);

    public async Task PresentedAsync(IPresentation component, Func<bool> shown, Func<string> describe)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            if (Shows(shown))
            {
                reached.TrySetResult();
            }
        }

        void OnPresented(object? sender, Presented presented) => Check();

        await InvokeAsync(
            () =>
            {
                component.Presented += OnPresented;
                Check();
            },
            CancellationToken.None);

        try
        {
            await reached.Task.WaitAsync(Patience);
        }
        catch (TimeoutException)
        {
            var state = await ReadAsync(() => $"revision {component.Revision}, {Describe(describe)}");

            throw new TimeoutException($"{component.GetType().Name} did not present the expected state within {Patience.TotalSeconds:0} s. Last state: {state}");
        }
        finally
        {
            await InvokeAsync(() => component.Presented -= OnPresented, CancellationToken.None);
        }
    }

    private static bool Shows(Func<bool> shown)
    {
        try
        {
            return shown();
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
        {
            return false;
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
