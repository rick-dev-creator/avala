using Avala.Forges.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Tests.Accounts;
using Avala.Workbench.Tests.RepositoryChoice;
using Avalonia.Controls;
using Avalonia.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class CloneSheetViewScripts(HeadlessUi ui)
{
    private const string Pasted = "https://git.example.com/octo/ledger.git";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSheetShowsASearchFieldTheAccountsAsSegmentsTheRepositoriesAndWhereTheyGoAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCloneSheetViewModel());

            Assert.Equal(["oc•••••", "ad•••••"], view.All<TextBlock>().Where(text => text.Name == "SourceLogin").Select(text => text.Text));
            Assert.Equal(["shop", "design-system", "ledger"], view.All<TextBlock>().Where(text => text.Name == "RepositoryName").Select(text => text.Text));
            Assert.Equal(("~/Projects", "Search or paste a URL", false), (view.TextOf("Destination"), view.Find<TextBox>("CloneSearch").PlaceholderText, view.Shows("CloneAddress")));
        }, Cancellation);

    [Fact]
    public Task PastingAnAddressShowsItAsTheTopRowAndEnterClonesItAsync() =>
        ui.RunAsync(async () =>
        {
            var forges = new FakeSourceControl();
            var remotes = new FakeRemotes();
            var sheet = Choosing.Cloning(forges, remotes, new FakeProjects { Chosen = "/home/ada/Projects" }, new FakeFolderPicker());
            await sheet.StartCommand.ExecuteAsync(null);
            var view = Screen.Show(sheet).Settle();

            view.Type("CloneSearch", Pasted);
            var row = (view.Shows("CloneAddress"), view.TextOf("CloneAddressText"));
            view.Press(Key.Enter);
            await (sheet.CloneAddressCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal((true, $"Clone {Pasted}"), row);
            Assert.Equal([(Pasted, "/home/ada/Projects", "ledger")], remotes.Cloned);
        }, Cancellation);

    [Fact]
    public Task AnErrorShowsItsOneFixButtonAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCloneSheetViewModel { Repositories = [], Error = ClonePhrases.Listing(ForgeError.Unauthorized), FixLabel = ClonePhrases.Connect });

            Assert.Equal((true, "Connect", ClonePhrases.Listing(ForgeError.Unauthorized)), (view.Shows("CloneFix"), view.Find<Button>("CloneFix").Content, view.TextOf("CloneError")));
            Assert.False(view.Shows("CloneFailure"));
        }, Cancellation);
}

public sealed class RemoteRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsNameMaskedOwnerPrivacyAndAgeAndAClickClonesItAsync() =>
        ui.RunAsync(() =>
        {
            var cloned = 0;
            var row = new RemoteRowViewModel(
                new RemoteRepository(new RepositoryAddress("hub.example", "octo", "shop"), new Uri("https://hub.example/octo/shop"), Private: true, Archived: true, Choosing.Now.AddHours(-3), "https://hub.example/octo/shop.git"),
                Choosing.Now,
                new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() =>
                {
                    cloned++;

                    return Task.CompletedTask;
                }));
            var view = Screen.Show(row);

            view.Click("CloneRow");

            Assert.Equal(("shop", "oc•••••", "3h", true, true), (view.TextOf("RepositoryName"), view.TextOf("Owner"), view.TextOf("Updated"), view.Shows("Private"), view.Shows("Archived")));
            Assert.Equal(1, cloned);
        }, TestContext.Current.CancellationToken);
}

public sealed class CloneSourceViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnAccountSegmentShowsItsMaskedLoginAndWhetherItIsChosenAsync() =>
        ui.RunAsync(() =>
        {
            var chosen = new List<ForgeName>();
            var source = new CloneSourceViewModel(Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"), Kinds.Hub.Logo, chosen.Add) { IsSelected = true };
            var view = Screen.Show(source);

            view.Click("Source");

            Assert.Equal(("oc•••••", true), (view.TextOf("SourceLogin"), view.HasClass("Source", "selected")));
            Assert.Equal([new ForgeName("hub")], chosen);
        }, TestContext.Current.CancellationToken);
}
