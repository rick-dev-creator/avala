using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Tests.Accounts;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.RepositoryChoice;

public sealed class CloneSheetViewModelScripts
{
    private const string Pasted = "https://git.example.com/octo/ledger.git";

    private readonly FakeSourceControl forges = new();
    private readonly FakeRemotes remotes = new();
    private readonly FakeProjects projects = new() { Chosen = "/home/ada/Projects" };
    private readonly FakeFolderPicker picker = new() { Answer = "/home/ada/code" };

    public CloneSheetViewModelScripts()
    {
        forges.Accounts.AddRange([Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"), Kinds.Declared("cup", Kinds.Cup, CredentialSource.Cli, login: "ada")]);
        forges.Repositories["hub"] = Listed(Remote("octo", "shop", hours: 2, isPrivate: true), Remote("acme", "design-system", hours: 72), Remote("octo", "ledger", hours: 24 * 400));
        forges.Repositories["cup"] = Listed(Remote("ada", "notes", hours: 1));
    }

    [Fact]
    public async Task OpeningListsTheFirstAccountsRepositoriesMostRecentFirstWithOwnerPrivacyAndWhenTheyChangedAsync()
    {
        var sheet = await OpenedAsync();

        Assert.Equal([("octocat", true), ("ada", false)], sheet.Sources.Select(source => (source.Login, source.IsSelected)));
        Assert.Equal(
            [("shop", "octo", true, "2h"), ("design-system", "acme", false, "3d"), ("ledger", "octo", false, "Sep 2025")],
            sheet.Repositories.Select(row => (row.Name, row.Owner, row.IsPrivate, row.Updated)));
        Assert.Equal("/home/ada/Projects", sheet.Destination);
    }

    [Fact]
    public async Task SearchingFiltersTheListedRepositoriesWithoutAskingTheForgeAgainAsync()
    {
        var sheet = await OpenedAsync();

        sheet.Search = "ACME";

        Assert.Equal(["design-system"], sheet.Repositories.Select(row => row.Name));
        Assert.Equal([("hub", string.Empty)], forges.Listed);
    }

    [Fact]
    public async Task ChoosingAnotherAccountListsItsRepositoriesAsync()
    {
        var sheet = await OpenedAsync();

        sheet.Sources[1].SelectCommand.Execute(null);
        await sheet.Loading;

        Assert.Equal((false, true), (sheet.Sources[0].IsSelected, sheet.Sources[1].IsSelected));
        Assert.Equal(["notes"], sheet.Repositories.Select(row => row.Name));
    }

    [Fact]
    public async Task OneClickClonesARepositoryIntoTheProjectsFolderAndFillsTheFieldAsync()
    {
        var repositories = new FakeRepositories();
        var field = new RepositoryFieldViewModel(picker, repositories, new Avala.Workbench.Submitting.RecentRepositories(new JobBoard(), Scratching.Folders()), new RepositoryStarts(Choosing.Creation(picker, repositories, new FakePublishing()), Choosing.Cloning(forges, remotes, projects, picker)));
        await field.Cloning.StartCommand.ExecuteAsync(null);

        await field.Cloning.Repositories[0].CloneCommand.ExecuteAsync(null);

        Assert.Equal([("https://hub.example/octo/shop.git", "/home/ada/Projects", "shop")], remotes.Cloned);
        Assert.Equal(("/home/ada/Projects/shop", false), (field.Repository, field.Cloning.IsOpen));
    }

    [Fact]
    public async Task TheFirstCloneAsksOnceWhereProjectsGoAsync()
    {
        projects.Chosen = Option<string>.None;
        var sheet = await OpenedAsync();

        await sheet.Repositories[0].CloneCommand.ExecuteAsync(null);
        await sheet.StartCommand.ExecuteAsync(null);
        await sheet.Repositories[1].CloneCommand.ExecuteAsync(null);

        Assert.Single(picker.Asked);
        Assert.Equal([("https://hub.example/octo/shop.git", "/home/ada/code", "shop"), ("https://hub.example/acme/design-system.git", "/home/ada/code", "design-system")], remotes.Cloned);
    }

    [Fact]
    public async Task APastedAddressBecomesTheTopRowAndClonesUnderItsOwnNameAsync()
    {
        var sheet = await OpenedAsync();

        sheet.Search = Pasted;
        var row = (sheet.Address, sheet.Repositories.Count);
        await sheet.CloneAddressCommand.ExecuteAsync(null);

        Assert.Equal((Pasted, 0), row);
        Assert.Equal([(Pasted, "/home/ada/Projects", "ledger")], remotes.Cloned);
    }

    [Theory]
    [InlineData(ForgeError.Unauthorized, "Connect")]
    [InlineData(ForgeError.Unreachable, "Check again")]
    public async Task AListThatCannotBeReadSaysWhyWithOneFixAsync(ForgeError error, string fix)
    {
        forges.Repositories["hub"] = error;
        var sheet = await OpenedAsync();
        var shown = (sheet.Error, sheet.FixLabel);
        Recover();

        await sheet.FixCommand.ExecuteAsync(null);
        await sheet.Loading;

        Assert.Equal((ClonePhrases.Listing(error), fix), shown);
        Assert.Equal(fix == "Connect", sheet.Connect.IsOpen);
        Assert.Equal(fix == "Connect" ? 0 : 1, sheet.Repositories.Count);
    }

    [Fact]
    public async Task WithoutAnAccountTheSheetOffersToConnectOneAsync()
    {
        forges.Accounts.Clear();

        var sheet = await OpenedAsync();
        await sheet.FixCommand.ExecuteAsync(null);

        Assert.Equal((ClonePhrases.Connect, true), (sheet.FixLabel, sheet.Connect.IsOpen));
    }

    [Fact]
    public async Task ARefusedCloneSaysWhyAndRetryClonesAgainAsync()
    {
        remotes.Refusal = WorkspaceFailure.RemoteUnauthorized;
        var sheet = await OpenedAsync();
        await sheet.Repositories[0].CloneCommand.ExecuteAsync(null);
        var refused = (sheet.IsOpen, sheet.Error, sheet.FixLabel);
        remotes.Refusal = Option<WorkspaceFailure>.None;

        await sheet.FixCommand.ExecuteAsync(null);

        Assert.Equal((true, RepositoryPhrases.Failure(WorkspaceFailure.RemoteUnauthorized), ClonePhrases.Retry), refused);
        Assert.Equal((2, false), (remotes.Cloned.Count, sheet.IsOpen));
    }

    [Fact]
    public async Task CancelStopsARunningCloneAndKeepsTheSheetOpenAsync()
    {
        remotes.Held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sheet = await OpenedAsync();

        var running = sheet.Repositories[0].CloneCommand.ExecuteAsync(null);
        await remotes.Started.Task;
        var cloning = (sheet.IsCloning, sheet.Status);
        sheet.CancelCommand.Execute(null);
        await running;

        Assert.Equal((true, "Cloning shop…"), cloning);
        Assert.Equal((false, true, RepositoryPhrases.CloneCancelled), (sheet.IsCloning, sheet.IsOpen, sheet.Error));
    }

    [Fact]
    public async Task ChangeChoosesAnotherProjectsFolderAsync()
    {
        var sheet = await OpenedAsync();

        await sheet.ChangeDestinationCommand.ExecuteAsync(null);

        Assert.Equal("/home/ada/code", sheet.Destination);
        Assert.Equal(["/home/ada/code"], projects.Choices);
    }

    private void Recover() => forges.Repositories["hub"] = Listed(Remote("octo", "shop", hours: 2));

    private static Result<IReadOnlyList<RemoteRepository>, ForgeError> Listed(params RemoteRepository[] repositories) => repositories;

    private static RemoteRepository Remote(string owner, string name, int hours, bool isPrivate = false) =>
        new(new RepositoryAddress("hub.example", owner, name), new Uri($"https://hub.example/{owner}/{name}"), isPrivate, Archived: false, Choosing.Now.AddHours(-hours), $"https://hub.example/{owner}/{name}.git");

    private async Task<CloneSheetViewModel> OpenedAsync()
    {
        var sheet = Choosing.Cloning(forges, remotes, projects, picker);
        await sheet.StartCommand.ExecuteAsync(null);

        return sheet;
    }
}

public sealed class RemoteRowViewModelScripts
{
    [Theory]
    [InlineData(0.5, "now")]
    [InlineData(45, "45m")]
    [InlineData(60 * 5, "5h")]
    [InlineData(60 * 24 * 9, "9d")]
    [InlineData(60 * 24 * 60, "Aug 13")]
    public void ARepositoryShowsHowLongAgoItChangedInAFewLetters(double minutes, string expected) =>
        Assert.Equal(expected, ClonePhrases.Since(Choosing.Now.AddMinutes(-minutes), Choosing.Now));
}

public sealed class CloneSourceViewModelScripts
{
    [Fact]
    public void AnAccountIsNamedByItsLoginOrByItsNameWhenTheLoginIsUnknown()
    {
        var named = new CloneSourceViewModel(Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"), Kinds.Hub.Logo, _ => { });
        var unknown = new CloneSourceViewModel(Kinds.Declared("work", Kinds.Hub, CredentialSource.Environment), Kinds.Hub.Logo, _ => { });

        Assert.Equal(("octocat", "work", Kinds.Hub.Logo), (named.Login, unknown.Login, named.Logo));
    }
}
