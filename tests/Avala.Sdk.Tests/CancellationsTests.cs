namespace Avala.Sdk.Tests;

public sealed class CancellationsTests
{
    [Fact]
    public async Task WaitingUntilCancelledStaysPendingUntilTheTokenIsCancelledAsync()
    {
        using var source = new CancellationTokenSource();

        var waiting = source.Token.UntilCancelledAsync();
        var pending = waiting.IsCompleted;
        await source.CancelAsync();

        Assert.False(pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task WaitingOnAnAlreadyCancelledTokenEndsCancelledAtOnceAsync()
    {
        var waiting = new CancellationToken(canceled: true).UntilCancelledAsync();

        Assert.True(waiting.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
