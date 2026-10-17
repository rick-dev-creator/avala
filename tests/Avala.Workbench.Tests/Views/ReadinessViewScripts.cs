using Avala.Components.Privacy;
using Avala.Testing.UI;
using Avala.Workbench.Readiness;

namespace Avala.Workbench.Tests.Views;

public sealed class ReadinessCardViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheCardListsEachItemAndIsHiddenWhenNothingIsMissingAsync() =>
        ui.RunAsync(() =>
        {
            var shown = Screen.Show(new DesignReadinessCardViewModel());
            var hidden = Screen.Show(new DesignReadinessCardViewModel { IsShown = false, Items = [] });

            Assert.True(shown.Shows("Card"));
            Assert.Contains("Commit", shown.VisibleTexts);
            Assert.Contains(ReadinessPhrases.RulesUncommitted, shown.VisibleTexts);
            Assert.False(hidden.Shows("Card"));
        }, Cancellation);
}

public sealed class ReadinessItemViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AMissingItemShowsItsMaskedTextItsActionAndItsCommandAndADoneOneOnlyItsCheckMarkAsync() =>
        ui.RunAsync(() =>
        {
            var missing = Screen.Show(new DesignReadinessItemViewModel { Command = "gh auth login" });
            var done = Screen.Show(new DesignReadinessItemViewModel { IsDone = true, Action = string.Empty, Text = new PersonalPhrase("Connected github.com") });

            Assert.Equal(("Connect github.com as oc••••• through gh to open pull requests and follow their checks", "gh auth login"), (missing.TextOf("Text"), missing.TextOf("Command")));
            Assert.Equal((true, true, false), (missing.Shows("Act"), missing.Shows("Missing"), missing.Shows("Done")));
            Assert.Equal((true, false, false), (done.Shows("Done"), done.Shows("Missing"), done.Shows("Act")));
        }, Cancellation);
}

public sealed class ReadinessAccountViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnAccountShowsItsMaskedLoginItsToolAndConnectAsync() =>
        ui.RunAsync(() =>
        {
            var account = Screen.Show(new DesignReadinessAccountViewModel());

            Assert.Equal(("oc•••••", "gh", true), (account.TextOf("Login"), account.TextOf("Tool"), account.Shows("Connect")));
        }, Cancellation);
}

public sealed class AgentPullRequestNoteViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheNoteShowsItsTextWithOpenSettingsAndHidesWhenNothingWasMentionedAsync() =>
        ui.RunAsync(() =>
        {
            var shown = Screen.Show(new DesignAgentPullRequestNoteViewModel());
            var hidden = Screen.Show(new DesignAgentPullRequestNoteViewModel { IsShown = false });

            Assert.Equal(("Pull request #123 was opened by the agent. Connect github.com in Settings to follow its checks.", true), (shown.TextOf("Text"), shown.Shows("OpenSettings")));
            Assert.False(hidden.Shows("Note"));
        }, Cancellation);
}
