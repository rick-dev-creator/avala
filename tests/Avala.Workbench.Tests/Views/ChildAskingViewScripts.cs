using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Views;

public sealed class ChildAskingViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheParentsConversationSaysWhichSubAgentWaitsForItAndWhatItAsksAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignChildAskingViewModel());

            Assert.Equal(
                ("Sub-agent \"Migrate the orders database\" is waiting for this job", "wants to run a command: dotnet ef database update", "Waiting for this job's answer"),
                (view.TextOf("Headline"), view.Find<Avalonia.Controls.SelectableTextBlock>("Asking").Text, view.TextOf("Status")));
        }, Cancellation);

    [Fact]
    public Task OnceTheRequestIsSettledTheEntrySaysHowAsync() =>
        ui.RunAsync(() =>
        {
            var entry = new ChildAskingEntry("child-asking", SessionId.New(), new ItemId("migrate"), "Migrate the database", "wants to run a command: dotnet ef database update");
            var asked = new ChildAskingViewModel(entry);
            var view = Screen.Show(asked);

            asked.Update(entry with { State = ChildAskingState.Passed, Passed = PassReason.BeyondParent });
            view.Settle();
            var passed = view.TextOf("Status");
            asked.Update(entry with { State = ChildAskingState.Allowed });
            view.Settle();

            Assert.Equal("Went to you: its parent's own rules do not allow it", passed);
            Assert.Equal(("Allowed by this job", false), (view.TextOf("Status"), asked.IsWaiting));
        }, Cancellation);
}
