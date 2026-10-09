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

    private sealed class Noting(List<string> ran, string name) : IStartupTask
    {
        public Task RunAsync(CancellationToken cancellationToken)
        {
            ran.Add(name);

            return Task.CompletedTask;
        }
    }
}
