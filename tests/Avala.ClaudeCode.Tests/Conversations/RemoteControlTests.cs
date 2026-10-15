using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class RemoteControlTests
{
    private const string Link = "https://claude.ai/code/session_01XLwi39hWkad8C1AaNLEP69";

    [Fact]
    public void TurningRemoteControlOnAsksClaudeCodeAndReportsTheLinkThenTheConnectedDevice()
    {
        var talk = new Talk();

        talk.Take(talk.Conversation.ControlRemotely(on: true));
        var request = talk.Sent.Single();
        talk.Receive(Cli.Bridge("ready"), Cli.RemoteAnswer((string)request["request_id"]!, Link), Cli.Bridge("connected"));

        Assert.Equal(("control_request", "remote_control", true), ((string?)request["type"], (string?)request["request"]!["subtype"], (bool?)request["request"]!["enabled"]));
        Assert.Equal(
            [RemoteState.Starting, RemoteState.Ready, RemoteState.Ready, RemoteState.Connected],
            talk.Events.OfType<RemoteControlChanged>().Select(changed => changed.Control.State));
        Assert.Equal(Option<string>.Some(Link), talk.Events.OfType<RemoteControlChanged>().Last().Control.Link);
        Assert.Equal(RemoteState.Connected, talk.Conversation.Remote);
    }

    [Fact]
    public void TurningRemoteControlOffAsksClaudeCodeToEndItAndReportsItOff()
    {
        var talk = new Talk();
        talk.Take(talk.Conversation.ControlRemotely(on: true));
        talk.Drain();

        talk.Take(talk.Conversation.ControlRemotely(on: false));

        Assert.Equal(false, (bool?)talk.Sent.Single()["request"]!["enabled"]);
        Assert.Equal([new RemoteControlChanged(talk.Session, RemoteControl.Off)], talk.Events);
    }

    [Fact]
    public void ARefusedRemoteControlIsReportedWithClaudeCodesReason()
    {
        var talk = new Talk();
        talk.Take(talk.Conversation.ControlRemotely(on: true));
        var request = (string)talk.Sent.Single()["request_id"]!;

        talk.Receive(Cli.RemoteRefusal(request, "Remote Control requires claude.ai subscription auth."));

        var refused = talk.Events.OfType<RemoteControlChanged>().Last().Control;
        Assert.Equal((RemoteState.Failed, Option<string>.Some("Remote Control requires claude.ai subscription auth.")), (refused.State, refused.Problem));
    }

    [Fact]
    public void AMessageFromADeviceWhileIdleStartsATurnThatEndsWithItsResult()
    {
        var talk = new Talk();
        var command = Guid.NewGuid().ToString();

        talk.Receive(
            Cli.Lifecycle(command, "queued"),
            Cli.Lifecycle(command, "started"),
            Cli.Init(),
            Cli.Replayed("Add a second line saying bye to hello.txt", fromDevice: true, command),
            Cli.Said("msg_1", "text", "Done."),
            Cli.Result(0.002m));

        var started = Assert.IsType<TurnStarted>(talk.Events[0]);
        Assert.Equal(new RemoteMessageArrived(talk.Session, started.Turn, "Add a second line saying bye to hello.txt") { BeganTurn = true }, talk.Events.OfType<RemoteMessageArrived>().Single());
        Assert.Equal(new TurnCompleted(talk.Session, started.Turn, TurnOutcome.Finished), talk.Events[^1]);
    }

    [Fact]
    public void ClaudeCodeEchoingAvalasOwnMessageAddsNothing()
    {
        var talk = new Talk().Begin("Add refunds").Receive(Cli.Init());
        talk.Drain();

        talk.Receive(Cli.Replayed("Add refunds", fromDevice: false));

        Assert.Empty(talk.Events);
    }

    [Fact]
    public void AMessageFromADeviceDuringATurnJoinsTheRunningTurn()
    {
        var talk = new Talk().Begin("Add refunds").Receive(Cli.Init());
        talk.Drain();

        talk.Receive(Cli.Replayed("Also cover partial refunds", fromDevice: true));

        Assert.Equal([new RemoteMessageArrived(talk.Session, talk.Turn, "Also cover partial refunds")], talk.Events);
    }
}
