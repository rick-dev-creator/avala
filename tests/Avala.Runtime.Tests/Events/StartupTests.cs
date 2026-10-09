using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Runtime.Tests.Events;

public sealed class StartupTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartupCompletedIsPublishedOnceEveryStartupTaskRanAsync()
    {
        using var data = new TemporaryFolder();
        var ran = new List<string>();
        await using var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddRuntime(new AvalaPaths(data.Path))
            .AddSingleton<IStartupTask>(new Noting(ran, "first"))
            .AddSingleton<IStartupTask>(new Noting(ran, "second"))
            .BuildServiceProvider();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var completions = new EventWatch<StartupCompleted>(services.GetRequiredService<IEventFeed>().SubscribeAsync<StartupCompleted>(lifetime.Token), lifetime.Token);
        var running = services.RunAsync(lifetime.Token);

        _ = await completions.UntilAsync(_ => true);

        Assert.Equal(["first", "second"], ran);
        await lifetime.CancelAsync();
        await running;
    }

    [Fact]
    public async Task StoppingWhileAStartupTaskRunsEndsTheHostQuietlyAndSkipsTheRestAsync()
    {
        using var data = new TemporaryFolder();
        var ran = new List<string>();
        var blocking = new Blocking();
        await using var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddRuntime(new AvalaPaths(data.Path))
            .AddSingleton<IStartupTask>(blocking)
            .AddSingleton<IStartupTask>(new Noting(ran, "after"))
            .BuildServiceProvider();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var running = services.RunAsync(lifetime.Token);

        await blocking.Started.Task.WaitAsync(Cancellation);
        await lifetime.CancelAsync();
        await running;

        Assert.Empty(ran);
    }

    private sealed class Blocking : IStartupTask
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Started.SetResult();
            await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class Noting(List<string> ran, string name) : IStartupTask
    {
        public Task RunAsync(CancellationToken cancellationToken)
        {
            ran.Add(name);

            return Task.CompletedTask;
        }
    }
}
