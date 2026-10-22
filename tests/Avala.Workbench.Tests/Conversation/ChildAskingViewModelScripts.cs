using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ChildAskingViewModelScripts
{
    private static readonly ChildAskingEntry Asked =
        new("child-asking", SessionId.New(), new ItemId("database"), "Choose the orders database\nfor the service", "asks: Choose a database");

    private static readonly ChildAskingEntry AskedYou =
        new("child-asking", SessionId.New(), new ItemId("migrate"), "Migrate the database\nwith care", "dotnet ef database update") { Permission = new ChildPermission(ItemKind.Command, "dotnet ef database update"), Harness = "Claude Code" };

    [Theory]
    [InlineData("Waiting", "Waiting for this job's answer", true)]
    [InlineData("Answered", "Answered by this job", false)]
    [InlineData("Declined", "Declined by this job", false)]
    [InlineData("Withdrawn", "No longer waiting", false)]
    public void TheParentsConversationSaysWhichSubAgentAsksItWhatAndHowItAnswered(string state, string status, bool waiting) =>
        ViewModelScript.Given(new ChildAskingViewModel(Asked))
            .When(asked => asked.Update(Asked with { State = Enum.Parse<ChildAskingState>(state) }))
            .Then(asked => Assert.Equal(
                ("Sub-agent \"Choose the orders database\" is waiting for this job", "asks: Choose a database", status, waiting, true),
                (asked.Headline, asked.Asking, asked.Status, asked.IsWaiting, asked.IsShown)));

    [Theory]
    [InlineData("Waiting", "Waiting for you: answer it in the sub-agent's conversation or the Delegation pane", true)]
    [InlineData("Allowed", "Allowed by you", false)]
    [InlineData("Denied", "Denied by you", false)]
    public void TheParentsConversationNamesTheSubAgentAndHarnessWhosePermissionWaitsForYouAndHowYouAnswered(string state, string status, bool waiting) =>
        ViewModelScript.Given(new ChildAskingViewModel(AskedYou))
            .When(asked => asked.Update(AskedYou with { State = Enum.Parse<ChildAskingState>(state) }))
            .Then(asked => Assert.Equal(
                ("Sub-agent \"Migrate the database\" on Claude Code is asking you", "wants to run dotnet ef database update", status, waiting),
                (asked.Headline, asked.Asking, asked.Status, asked.IsWaiting)));
}
