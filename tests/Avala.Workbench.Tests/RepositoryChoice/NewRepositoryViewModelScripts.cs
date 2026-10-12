using Avala.Workbench.RepositoryChoice;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.RepositoryChoice;

public sealed class NewRepositoryViewModelScripts
{
    private readonly FakeFolderPicker picker = new() { Answer = "/home/ada/code" };
    private readonly FakeRepositories repositories = new();

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
        creation.Created += (_, path) => created.Add(path);
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

    private NewRepositoryViewModel Creation() => new(picker, repositories);
}
