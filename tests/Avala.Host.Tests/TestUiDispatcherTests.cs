using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class TestUiDispatcherTests
{
    [Fact]
    public async Task WhileHostedItsWorkRunsOnTheHostsThreadInsteadOfItsOwnAsync()
    {
        using var dispatcher = new TestUiDispatcher();
        var host = 0;
        var thread = 0;
        var invoked = Task.CompletedTask;

        await dispatcher.HostAsync(
            drain =>
            {
                host = Environment.CurrentManagedThreadId;
                invoked = dispatcher.InvokeAsync(() => thread = Environment.CurrentManagedThreadId, TestContext.Current.CancellationToken).AsTask();
                drain();

                return Task.CompletedTask;
            });

        await invoked;
        Assert.Equal(host, thread);
    }
}
