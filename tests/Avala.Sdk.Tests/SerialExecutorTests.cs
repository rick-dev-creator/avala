namespace Avala.Sdk.Tests;

public sealed class SerialExecutorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunsOneOperationAtATimeInQueueOrderAsync()
    {
        await using var executor = new SerialExecutor();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new List<int>();

        var first = executor.RunAsync(
            async _ =>
            {
                started.Add(1);
                await release.Task;

                return 1;
            },
            Cancellation);
        var second = executor.RunAsync(_ => Task.FromResult(Started(started, 2)), Cancellation);

        Assert.False(second.IsCompleted);
        release.SetResult();

        var results = await Task.WhenAll(first, second).WaitAsync(Patience, Cancellation);

        Assert.Equal([1, 2], results);
        Assert.Equal([1, 2], started);
    }

    [Fact]
    public async Task AFailingOperationFaultsOnlyItsOwnTaskAsync()
    {
        await using var executor = new SerialExecutor();

        var failing = executor.RunAsync<int>(_ => throw new InvalidOperationException("broken"), Cancellation);
        var next = executor.RunAsync(_ => Task.FromResult(2), Cancellation);

        Assert.Equal("broken", (await Assert.ThrowsAsync<InvalidOperationException>(() => failing.WaitAsync(Patience, Cancellation))).Message);
        Assert.Equal(2, await next.WaitAsync(Patience, Cancellation));
    }

    [Fact]
    public async Task AnOperationCancelledBeforeItsTurnNeverRunsAsync()
    {
        await using var executor = new SerialExecutor();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = new CancellationTokenSource();
        var ran = false;

        var blocking = executor.RunAsync(_ => release.Task, Cancellation);
        var skipped = executor.RunAsync(
            _ =>
            {
                ran = true;

                return Task.CompletedTask;
            },
            cancelled.Token);
        await cancelled.CancelAsync();
        release.SetResult();

        await blocking.WaitAsync(Patience, Cancellation);
        await Assert.ThrowsAsync<TaskCanceledException>(() => skipped.WaitAsync(Patience, Cancellation));
        Assert.False(ran);
    }

    [Fact]
    public async Task DisposingFinishesTheQueuedWorkAndRefusesNewWorkAsync()
    {
        var executor = new SerialExecutor();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = executor.RunAsync(
            async _ =>
            {
                await release.Task;

                return 1;
            },
            Cancellation);

        var disposing = executor.DisposeAsync().AsTask();
        var refused = executor.RunAsync(_ => Task.FromResult(2), Cancellation);
        release.SetResult();
        await disposing.WaitAsync(Patience, Cancellation);

        Assert.Equal(1, await queued);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => refused);
    }

    private static int Started(List<int> started, int operation)
    {
        started.Add(operation);

        return operation;
    }
}
