using Avala.Forges.Contracts;
using Avala.Workbench.RepositoryChoice;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.RepositoryChoice;

public sealed class NewRepositoryViewModelScripts
{
    private readonly FakeFolderPicker picker = new() { Answer = "/home/ada/code" };
    private readonly FakeRepositories repositories = new();
    private readonly FakePublishing publishing = new();

    [Fact]
    public async Task CancellingTheParentPickerOpensNoNewRepositoryAsync()
    {
        picker.Answer = default;
        var creation = Creation();

        await creation.StartCommand.ExecuteAsync(null);

        Assert.Equal((false, false), (creation.IsOpen, creation.CreateCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ANameIsNeededBeforeTheRepositoryCanBeCreatedAsync()
    {
        var creation = Creation();
        await creation.StartCommand.ExecuteAsync(null);
        var unnamed = creation.CreateCommand.CanExecute(null);

        creation.Name = "invoices";

        Assert.Equal((false, true), (unnamed, creation.CreateCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ARefusedNewRepositoryStaysOpenWithTheReasonAsync()
    {
        var creation = Creation();
        var created = new List<string>();
        creation.Created += (_, made) => created.Add(made.Path);
        await creation.StartCommand.ExecuteAsync(null);
        repositories.Refusal = WorkspaceFailure.FolderExists;
        creation.Name = "shop";

        await creation.CreateCommand.ExecuteAsync(null);

        Assert.Equal((true, "Something with that name is already there: choose another name.", 0), (creation.IsOpen, creation.Error, created.Count));
    }

    [Fact]
    public async Task CancellingANewRepositoryClosesItAndForgetsTheNameAsync()
    {
        var creation = Creation();
        await creation.StartCommand.ExecuteAsync(null);
        creation.Name = "invoices";

        creation.CancelCommand.Execute(null);

        Assert.Equal((false, string.Empty), (creation.IsOpen, creation.Name));
    }

    [Fact]
    public async Task APublishedNewRepositoryIsCreatedThenPublishedPrivateUnderItsNameToTheChosenForgeAsync()
    {
        var creation = Creation();
        var created = new List<RepositoryCreated>();
        creation.Created += (_, made) => created.Add(made);
        await creation.StartCommand.ExecuteAsync(null);
        creation.IsPublishing = true;
        await creation.Publishing.Loading;
        creation.Name = "invoices";

        await creation.CreateCommand.ExecuteAsync(null);

        Assert.Equal([("/home/ada/code/invoices", FakePublishing.GitHub.Forge, new RepositoryDraft("invoices", Private: true))], publishing.Published);
        Assert.Equal(
            "Created /home/ada/code/invoices: an empty git repository with a first commit. Published ada/invoices on github and pushed main; origin is git@github.com:ada/invoices.git.",
            Assert.Single(created).Notice);
    }

    [Fact]
    public async Task ANewRepositoryWhosePublishingIsRefusedIsStillCreatedAndSaysWhyAsync()
    {
        var creation = Creation();
        var created = new List<RepositoryCreated>();
        creation.Created += (_, made) => created.Add(made);
        await creation.StartCommand.ExecuteAsync(null);
        creation.IsPublishing = true;
        await creation.Publishing.Loading;
        publishing.Refusal = ForgeError.NameTaken;
        creation.Name = "invoices";

        await creation.CreateCommand.ExecuteAsync(null);

        Assert.Equal("/home/ada/code/invoices", Assert.Single(created).Path);
        Assert.EndsWith("A repository with that name already exists on the forge: choose another name.", created[0].Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAForgeThatCreatesRepositoriesPublishingSaysHowToAddOneAndCannotCreateAsync()
    {
        publishing.Targets.Clear();
        var creation = Creation();
        await creation.StartCommand.ExecuteAsync(null);
        creation.Name = "invoices";

        creation.IsPublishing = true;
        await creation.Publishing.Loading;

        Assert.Equal((PublishPhrases.NoTargets, false), (creation.Publish.Problem, creation.CreateCommand.CanExecute(null)));
    }

    private NewRepositoryViewModel Creation() => Choosing.Creation(picker, repositories, publishing);
}
