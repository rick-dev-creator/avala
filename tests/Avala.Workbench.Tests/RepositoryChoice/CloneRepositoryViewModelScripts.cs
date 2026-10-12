using Avala.Workbench.Board;
using Avala.Workbench.RepositoryChoice;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.RepositoryChoice;

public sealed class CloneRepositoryViewModelScripts
{
    private const string Shop = "git@github.com:octo/shop.git";

    private static readonly string Beside = Path.GetDirectoryName("/home/ada/code/notes")!;

    private readonly FakeFolderPicker picker = new() { Answer = "/home/ada/src" };
    private readonly FakeRemotes remotes = new();

    [Fact]
    public async Task ACloneStartedBesideARepositoryGoesNextToItWithoutAskingAsync()
    {
        var cloning = Cloning("/home/ada/code/notes");

        await cloning.StartCommand.ExecuteAsync(null);

        Assert.Equal((true, Beside, 0), (cloning.IsOpen, cloning.Parent, picker.Asked.Count));
    }

    [Fact]
    public async Task ACloneStartedWithoutARepositoryAsksWhereItGoesAsync()
    {
        var cloning = Cloning(string.Empty);

        await cloning.StartCommand.ExecuteAsync(null);

        Assert.Equal(("/home/ada/src", RepositoryPhrases.CloneParentTitle), (cloning.Parent, Assert.Single(picker.Asked).Title));
    }

    [Fact]
    public async Task ThePastedAddressNamesTheFolderUntilANameIsTypedAsync()
    {
        var cloning = await OpenedAsync();

        cloning.Url = Shop;
        var named = cloning.Name;
        cloning.Name = "storefront";
        cloning.Url = "https://codeberg.org/octo/ledger.git";

        Assert.Equal(("shop", "storefront"), (named, cloning.Name));
    }

    [Theory]
    [InlineData("https://github.com/octo/shop.git", "shop")]
    [InlineData("git@github.com:octo/shop.git", "shop")]
    [InlineData("ssh://git@git.example.com:2222/octo/shop.git", "shop")]
    [InlineData("https://codeberg.org/octo/shop/", "shop")]
    [InlineData("/srv/git/shop.git", "shop")]
    public void TheFolderOfACloneIsNamedAfterTheRepositoryInItsAddress(string url, string name) =>
        Assert.Equal(name, CloneNames.From(url));

    [Fact]
    public async Task AnAddressAFolderAndANameAreNeededBeforeCloningAsync()
    {
        var cloning = await OpenedAsync();
        var empty = cloning.CloneCommand.CanExecute(null);

        cloning.Url = Shop;

        Assert.Equal((false, true), (empty, cloning.CloneCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ACloneFillsTheRepositoryFieldAndSaysWhereItCameFromAsync()
    {
        var field = Choosing.Field(new JobBoard(), picker, new FakeRepositories(), remotes);
        field.Repository = "/home/ada/code/notes";
        await field.Cloning.StartCommand.ExecuteAsync(null);
        field.Cloning.Url = Shop;

        await field.Cloning.CloneCommand.ExecuteAsync(null);

        Assert.Equal([(Shop, Beside, "shop")], remotes.Cloned);
        Assert.Equal(($"{Beside}/shop", $"Cloned {Shop} into {Beside}/shop.", false), (field.Repository, field.Notice, field.Cloning.IsOpen));
    }

    [Fact]
    public async Task ARefusedCloneStaysOpenWithTheReasonAsync()
    {
        remotes.Refusal = WorkspaceFailure.RemoteUnauthorized;
        var cloning = await OpenedAsync();
        cloning.Url = Shop;

        await cloning.CloneCommand.ExecuteAsync(null);

        Assert.Equal((true, RepositoryPhrases.Failure(WorkspaceFailure.RemoteUnauthorized)), (cloning.IsOpen, cloning.Error));
    }

    [Fact]
    public async Task ARunningCloneShowsWhatItDoesAndCancelStopsItKeepingThePanelOpenAsync()
    {
        remotes.Held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloning = await OpenedAsync();
        cloning.Url = Shop;

        var running = cloning.CloneCommand.ExecuteAsync(null);
        await remotes.Started.Task;
        var shown = (cloning.IsCloning, cloning.Status, cloning.CloneCommand.CanExecute(null));
        cloning.CancelCommand.Execute(null);
        await running;

        Assert.Equal((true, $"Cloning {Shop} into {Path.Combine(Beside, "shop")}…", false), shown);
        Assert.Equal((false, true, RepositoryPhrases.CloneCancelled), (cloning.IsCloning, cloning.IsOpen, cloning.Error));
    }

    private CloneRepositoryViewModel Cloning(string near) => new(picker, remotes) { Near = near };

    private async Task<CloneRepositoryViewModel> OpenedAsync()
    {
        var cloning = Cloning("/home/ada/code/notes");
        await cloning.StartCommand.ExecuteAsync(null);

        return cloning;
    }
}
