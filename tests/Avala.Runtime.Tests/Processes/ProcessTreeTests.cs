using System.Collections.Immutable;
using System.Globalization;
using Avala.Runtime.Processes;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Runtime.Tests.Processes;

public sealed class ProcessTreeTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProcessStartedThroughATreeRunsWithTheTreeAndContributedVariablesAsync()
    {
        using var home = new TemporaryFolder();
        var port = Workloads.FreePort().ToString(CultureInfo.InvariantCulture);
        await using var services = Runtime(new Contributing("AVALA_PORT", port));
        var tree = await Trees(services).OpenAsync(home.Path, Cancellation);

        var (_, marker) = await Workloads.StartAsync(tree, Workloads.Command("env", ProcessTreeId.Variable), Cancellation);
        var (server, listening) = await Workloads.StartAsync(tree, Workloads.Holding("serve"), Cancellation);
        using var _ = server;

        Assert.Equal(tree.Id.Value.ToString(), marker);
        Assert.Equal($"listening {port}", listening);
        Assert.Equal((port, tree.Id.Value.ToString()), (tree.Environment["AVALA_PORT"], tree.Environment[ProcessTreeId.Variable]));
    }

    [Fact]
    public async Task AProcessThatOutlivesItsParentStaysInTheTreeAsync()
    {
        using var home = new TemporaryFolder();
        await using var services = Runtime();
        var tree = await Trees(services).OpenAsync(home.Path, Cancellation);

        var (parent, spawned) = await Workloads.StartAsync(tree, Workloads.Holding("spawn"), Cancellation);
        await parent.WaitForExitAsync(Cancellation);
        parent.Dispose();
        var orphan = int.Parse(spawned["spawned ".Length..], CultureInfo.InvariantCulture);

        Assert.Contains(orphan, (await tree.MembersAsync(Cancellation)).Select(member => member.Id));
    }

    [Fact]
    public async Task ClosingATreeKillsEveryProcessInItAndNothingElseAsync()
    {
        using var home = new TemporaryFolder();
        await using var services = Runtime();
        var trees = Trees(services);
        var closing = await trees.OpenAsync(home.Path, Cancellation);
        var other = await trees.OpenAsync(home.Path, Cancellation);
        var (held, _) = await Workloads.StartAsync(closing, Workloads.Holding("hold"), Cancellation);
        var (parent, spawned) = await Workloads.StartAsync(closing, Workloads.Holding("spawn"), Cancellation);
        var (bystander, _) = await Workloads.StartAsync(other, Workloads.Holding("hold"), Cancellation);
        await parent.WaitForExitAsync(Cancellation);
        var orphan = int.Parse(spawned["spawned ".Length..], CultureInfo.InvariantCulture);

        var survivors = await trees.CloseAsync(closing.Id, Cancellation);
        await held.WaitForExitAsync(Cancellation);

        Assert.Empty(survivors);
        Assert.True(await Workloads.IsGoneAsync(orphan), $"Process {orphan} survived its tree");
        Assert.Equal([bystander.Id], (await other.MembersAsync(Cancellation)).Select(member => member.Id));
        Assert.Equal(Option<IProcessTree>.None, trees.Find(closing.Id));
    }

    [Fact]
    public async Task ClosingATreeAsksItsProcessesToEndAndKillsThoseThatIgnoreItOnceTheGracePassesAsync()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no signal that asks a console process to end from outside its console.");
        using var home = new TemporaryFolder();
        var clock = new FakeTimeProvider();
        await using var services = Runtime(clock);
        var trees = Trees(services);
        var tree = await trees.OpenAsync(home.Path, Cancellation);
        var (polite, _) = await Workloads.StartAsync(tree, Workloads.Holding("hold"), Cancellation);
        var (stubborn, _) = await Workloads.StartAsync(tree, Workloads.Holding("stubborn"), Cancellation);
        using var ending = polite;
        using var refusing = stubborn;

        var closing = trees.CloseAsync(tree.Id, Cancellation).AsTask();
        var refusal = await stubborn.StandardOutput.ReadLineAsync(Cancellation);
        await polite.WaitForExitAsync(Cancellation);
        var waiting = (closing.IsCompleted, stubborn.HasExited);
        clock.Advance(ProcessTree.Grace);
        var survivors = await closing;
        await stubborn.WaitForExitAsync(Cancellation);

        Assert.Equal(("ignored", (false, false)), (refusal, waiting));
        Assert.Empty(survivors);
    }

    [Fact]
    public async Task AProcessTheRunnerStartsInATreesFolderJoinsThatTreeAsync()
    {
        using var home = new TemporaryFolder();
        await using var services = Runtime();
        var tree = await Trees(services).OpenAsync(home.Path, Cancellation);
        var command = Workloads.Command("env", ProcessTreeId.Variable);

        var outcome = Outcomes.Succeeds(await services.GetRequiredService<IProcessRunner>().RunAsync(
            new ProcessRequest(command.FileName, [.. command.ArgumentList], home.Path),
            Cancellation));

        Assert.Equal(tree.Id.Value.ToString(), outcome.Output.Trim());
    }

    [Fact]
    public async Task AMissingExecutableIsNotFoundInATreeAsync()
    {
        using var home = new TemporaryFolder();
        await using var services = Runtime();
        var tree = await Trees(services).OpenAsync(home.Path, Cancellation);

        Assert.Equal(ProcessError.NotFound, Outcomes.FailsWith(tree.Start(new System.Diagnostics.ProcessStartInfo("avala-no-such-executable"))));
    }

    [Fact]
    public async Task AListeningMemberIsReportedWithItsPortAsync()
    {
        using var home = new TemporaryFolder();
        await using var services = Runtime();
        var tree = await Trees(services).OpenAsync(home.Path, Cancellation);
        var (server, listening) = await Workloads.StartAsync(tree, Workloads.Holding("serve"), Cancellation);
        using var _ = server;
        var port = int.Parse(listening["listening ".Length..], CultureInfo.InvariantCulture);

        var listeners = await services.GetRequiredService<IListeningPorts>().ListAsync(new HashSet<int> { server.Id }, Cancellation);

        Assert.Contains(new Listener(port, server.Id), listeners);
    }

    [Fact]
    public async Task WithoutContainmentATreeStillKnowsTheProcessesItStartedWhileTheyRunAsync()
    {
        using var container = new Containment.RootsContainer();
        using var held = Outcomes.Succeeds(ProcessStarts.Start(container.Prepare(Workloads.Holding("hold"))));
        container.Adopt(held);

        var running = await container.MemberIdsAsync(Cancellation);
        held.Kill();
        await held.WaitForExitAsync(Cancellation);

        Assert.Equal([held.Id], running);
        Assert.Empty(await container.MemberIdsAsync(Cancellation));
    }

    private static ServiceProvider Runtime(params IProcessEnvironment[] environments)
    {
        var services = new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath()));

        foreach (var environment in environments)
        {
            services.AddSingleton(environment);
        }

        return services.BuildServiceProvider();
    }

    private static ServiceProvider Runtime(TimeProvider clock) =>
        new ServiceCollection().AddSingleton(clock).AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider();

    private static IProcessTrees Trees(ServiceProvider services) => services.GetRequiredService<IProcessTrees>();

    private sealed class Contributing(string name, string value) : IProcessEnvironment
    {
        public ValueTask<IReadOnlyDictionary<string, string>> ForAsync(string home, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(ImmutableDictionary<string, string>.Empty.Add(name, value));
    }
}
