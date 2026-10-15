using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Sessions;
using Avala.Agents.Tests.Connections;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Agents.Tests.Sessions;

public sealed class RemoteControlTests
{
    private static readonly AgentRequest Request = new("/worktrees/1");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TurningRemoteControlOnAsksTheSessionAndReportsItsLinkAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply) { Capabilities = ScriptedAgentProvider.Declared.With(new ControlsRemotely()) };
        var bus = new RecordingBus();
        await using var agents = Agents(bus, provider);
        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        Assert.Equal(RemoteState.Ready, Outcomes.Succeeds(await agents.ControlRemotelyAsync(opened.Session, on: true, Cancellation)));

        var reported = await bus.WaitForAsync<RemoteControlReported>(_ => true, Cancellation);
        Assert.Equal([true], Assert.Single(provider.Sessions).RemoteControls);
        Assert.Equal(opened.Session, reported.Session);
        Assert.Equal(Option<string>.Some($"https://example.test/remote/{opened.Session.Value}"), reported.Control.Link);
        Assert.Empty(bus.Published.OfType<AgentActivity>());
    }

    [Fact]
    public async Task ASessionWhoseProviderCannotBeControlledRemotelyIsNeverAskedAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        await using var agents = Agents(new RecordingBus(), provider);
        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        Assert.Equal(AgentError.NotRemoteControllable, Outcomes.FailsWith(await agents.ControlRemotelyAsync(opened.Session, on: true, Cancellation)));
        Assert.Equal(AgentError.SessionClosed, Outcomes.FailsWith(await agents.ControlRemotelyAsync(SessionId.New(), on: true, Cancellation)));
        Assert.Empty(Assert.Single(provider.Sessions).RemoteControls);
    }

    [Fact]
    public async Task AMessageFromADeviceStartsATurnAndIsAnnouncedWithItsTextAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply) { Capabilities = ScriptedAgentProvider.Declared.With(new ControlsRemotely()) };
        var bus = new RecordingBus();
        await using var agents = Agents(bus, provider);
        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        await Assert.Single(provider.Sessions).SayFromDeviceAsync("Also cover partial refunds", Cancellation);

        var received = await bus.WaitForAsync<RemoteMessageReceived>(_ => true, Cancellation);
        var finished = await bus.WaitForAsync<TurnFinished>(_ => true, Cancellation);
        Assert.Equal(("Also cover partial refunds", true, opened.Session), (received.Text, received.BeganTurn, received.Session));
        Assert.Equal(received.Turn, finished.Turn);
    }

    private static AgentSessions Agents(RecordingBus bus, params IAgentProvider[] providers) =>
        new(Connected.Starter(Connected.Registry(providers), [], []), bus, TimeProvider.System, NullLogger<AgentSessions>.Instance);
}
