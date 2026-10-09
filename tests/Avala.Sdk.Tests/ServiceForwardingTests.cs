using Microsoft.Extensions.DependencyInjection;

namespace Avala.Sdk.Tests;

public sealed class ServiceForwardingTests
{
    [Fact]
    public void EveryContractForwardedToASingletonResolvesToThatOneInstance()
    {
        using var services = new ServiceCollection()
            .AddSingleton<Ledger>()
            .AddForwarded<IStartupTask, Ledger>()
            .AddForwarded<ILedger, Ledger>()
            .AddSingleton<IStartupTask, OtherTask>()
            .BuildServiceProvider();

        var ledger = services.GetRequiredService<Ledger>();

        Assert.Same(ledger, services.GetRequiredService<ILedger>());
        Assert.Collection(services.GetServices<IStartupTask>(), task => Assert.Same(ledger, task), task => Assert.IsType<OtherTask>(task));
    }

    private interface ILedger;

    private sealed class Ledger : IStartupTask, ILedger
    {
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OtherTask : IStartupTask
    {
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
