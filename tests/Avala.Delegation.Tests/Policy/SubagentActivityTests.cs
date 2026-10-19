using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Policy;
using Avala.Sdk;

namespace Avala.Delegation.Tests.Policy;

public sealed class SubagentActivityTests
{
    private static readonly SessionId Session = SessionId.New();

    private static readonly TurnId Turn = TurnId.New();

    private static readonly ItemId Survey = new("survey");

    private static readonly ItemId Build = new("build");

    [Fact]
    public void ASubagentWorksWaitsWhileOneOfItsStepsAsksAPermissionAndTakesItsOutcomeWhenItCompletes()
    {
        var started = SubagentActivity.None
            .Apply(new ItemStarted(Session, Turn, Survey, ItemKind.Subagent, "Subagent: Survey the greeting"))
            .Apply(new ItemStarted(Session, Turn, Build, ItemKind.Subagent, "Check the build"))
            .Apply(new ItemBackgrounded(Session, Turn, Survey));
        var asking = started
            .Apply(new ItemStarted(Session, Turn, new ItemId("npm"), ItemKind.Command, "npm install") { Parent = Build })
            .Apply(new PermissionRequested(Session, Turn, new ItemId("npm"), "npm install", ItemKind.Command, "npm install"));
        var answered = asking.Apply(new PermissionResolved(Session, Turn, new ItemId("npm"), PermissionAnswer.Allow));
        var completed = answered.Apply(new ItemCompleted(Session, Turn, Survey, ItemOutcome.Succeeded));

        Assert.Equal(["Survey the greeting", "Check the build"], started.Subagents.Select(agent => agent.Name));
        Assert.Equal([Asking.Nothing, Asking.Permission], asking.Subagents.Select(asking.AskingOf));
        Assert.Equal((Asking.Permission, Asking.Nothing), (asking.Asking, answered.Asking));
        Assert.Equal([Option<ItemOutcome>.Some(ItemOutcome.Succeeded), Option<ItemOutcome>.None], completed.Subagents.Select(agent => agent.Outcome));
    }

    [Fact]
    public void ASubagentStillOpenWhenItsSessionOrItsRunEndsIsAbandoned()
    {
        var other = SessionId.New();
        var running = SubagentActivity.None
            .Apply(new ItemStarted(Session, Turn, Survey, ItemKind.Subagent, "Survey"))
            .Apply(new ItemStarted(other, Turn, Build, ItemKind.Subagent, "Build"))
            .Apply(new FormRequested(other, Turn, new ItemId("ask"), new AgentForm(FormPurpose.Question, "Which?", string.Empty, [])));

        var sessionEnded = running.Ended(Session);
        var runEnded = running.Ended();

        Assert.Equal([Option<ItemOutcome>.Some(ItemOutcome.Abandoned), Option<ItemOutcome>.None], sessionEnded.Subagents.Select(agent => agent.Outcome));
        Assert.Equal(Asking.Answer, sessionEnded.Asking);
        Assert.All(runEnded.Subagents, agent => Assert.Equal(Option<ItemOutcome>.Some(ItemOutcome.Abandoned), agent.Outcome));
        Assert.Equal(Asking.Nothing, runEnded.Asking);
    }
}
