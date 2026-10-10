using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ChildAskingViewModelScripts
{
    private static readonly ChildAskingEntry Asked =
        new("child-asking", SessionId.New(), new ItemId("migrate"), "Migrate the database\nwith care", "wants to run a command: dotnet ef database update");

    [Theory]
    [InlineData("Waiting", null, "Waiting for this job's answer", true)]
    [InlineData("Allowed", null, "Allowed by this job", false)]
    [InlineData("Denied", null, "Denied by this job", false)]
    [InlineData("AnsweredByPerson", null, "Answered by you", false)]
    [InlineData("Passed", PassReason.ParentTimedOut, "Went to you: its parent did not answer in time", false)]
    public void TheParentsConversationSaysWhichSubAgentAsksWhatAndHowItWasSettled(string state, PassReason? reason, string status, bool waiting) =>
        ViewModelScript.Given(new ChildAskingViewModel(Asked))
            .When(asked => asked.Update(Asked with { State = Enum.Parse<ChildAskingState>(state), Passed = reason.ToOption() }))
            .Then(asked => Assert.Equal(
                ("Sub-agent \"Migrate the database\" is waiting for this job", "wants to run a command: dotnet ef database update", status, waiting, true),
                (asked.Headline, asked.Asking, asked.Status, asked.IsWaiting, asked.IsShown)));
}
