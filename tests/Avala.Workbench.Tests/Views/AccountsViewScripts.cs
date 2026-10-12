using Avala.Forges.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Accounts;
using Avala.Workbench.Settings;
using Avala.Workbench.Tests.Accounts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class AccountRowViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnAccountRowShowsItsLogoLoginHostAndSignInChipWithAStatusDotOrAddWhenDetectedAsync() =>
        ui.RunAsync(() =>
        {
            var signedIn = Screen.Show(new DesignAccountRowViewModel());
            var attention = Screen.Show(new DesignAccountRowViewModel { State = AccountState.Attention, Problem = "Token not set" });
            var detected = Screen.Show(new DesignAccountRowViewModel { State = AccountState.Detected });

            Assert.Equal(("octocat", "github.com", "gh"), (signedIn.TextOf("Login"), signedIn.TextOf("Host"), signedIn.Find<Border>("SignIn").Child is TextBlock chip ? chip.Text : null));
            Assert.NotNull(signedIn.Find<Avalonia.Controls.Shapes.Path>("Logo").Data);
            Assert.Equal((true, false, false), (signedIn.Shows("Status"), signedIn.HasClass("Status", "attention"), signedIn.Shows("Add")));
            Assert.Equal((true, "Token not set"), (attention.HasClass("Status", "attention"), attention.TextOf("Problem")));
            Assert.Equal((true, false), (detected.Shows("Add"), detected.Shows("Status")));
        }, Cancellation);

}

public sealed class ConnectSheetViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheConnectSheetOffersTheForgesAsSegmentsAndASignedOutCliItsCommandAndCheckAgainAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectSheetViewModel());

            Assert.Equal(["GitHub", "Gitea", "Forgejo"], view.All<TextBlock>().Where(text => text.Name == "KindName").Select(text => text.Text));
            Assert.Equal((false, true, true, "gh auth login"), (view.Shows("Host"), view.Shows("CheckAgain"), view.HasClass("CliDot", "attention"), view.Find<SelectableTextBlock>("SignInCommand").Text));
            Assert.False(view.Shows("TokenVariable"));
        }, Cancellation);

    [Fact]
    public Task ChoosingTheTokenAsksForTheNameOfItsVariableAfterADollarSignAsync() =>
        ui.RunAsync(() =>
        {
            var sheet = new DesignConnectSheetViewModel();
            var view = Screen.Show(sheet);

            view.Click("UseToken");

            var field = view.Find<TextBox>("TokenVariable");
            var text = field.GetVisualDescendants().OfType<TextPresenter>().Single();
            var prefix = view.Find("TokenPrefix");
            var before = prefix.TranslatePoint(new Point(prefix.Bounds.Width, 0), view.Window) is { } end && text.TranslatePoint(default, view.Window) is { } start && end.X <= start.X;
            Assert.Equal((true, false, "GITHUB_TOKEN"), (view.Shows("TokenVariable"), view.Shows("CliSignIn"), field.Text));
            Assert.Equal((true, "$", true), (view.Shows("TokenPrefix"), view.TextOf("TokenPrefix"), before));
        }, Cancellation);

}

public sealed class SourceControlViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ConnectOnTheSettingsPageOpensTheSheetOverThePageAndCancelClosesItAsync() =>
        ui.RunAsync(async () =>
        {
            var forges = new FakeSourceControl();
            forges.Accounts.Add(Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"));
            var accounts = new SourceControlViewModel(forges.Source, new ConnectSheetViewModel(forges.Source));
            await accounts.LoadAsync(Cancellation);
            var settings = new SettingsViewModel(new DesignRepositorySettingsViewModel(), new DesignMachineSettingsViewModel { SourceControl = accounts }, new DesignAppearanceViewModel(), new DesignAboutViewModel());
            var view = Screen.Show(settings, Avalonia.Styling.ThemeVariant.Dark, 1280, 1400).Settle();

            view.Find("Connect").BringIntoView();
            view.Settle().Click("Connect");
            await accounts.Connecting.Reading;
            view.Settle();
            var opened = view.Shows("ConnectSheet");
            view.Click("Cancel");

            Assert.True(opened);
            Assert.False(view.Shows("ConnectSheet"));
        }, Cancellation);
}

public sealed class ForgeKindViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ClickingAForgeSegmentSelectsItAsync() =>
        ui.RunAsync(() =>
        {
            var chosen = new List<string>();
            var kind = new ForgeKindViewModel(Kinds.Cup, chosen.Add);
            var view = Screen.Show(kind);

            view.Click("Kind");
            kind.IsSelected = true;
            view.Settle();

            Assert.Equal(["cup"], chosen);
            Assert.Equal(("Cup", true), (view.TextOf("KindName"), view.HasClass("Kind", "selected")));
        }, Cancellation);
}
