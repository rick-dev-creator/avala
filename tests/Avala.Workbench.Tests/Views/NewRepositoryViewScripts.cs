using Avala.Testing.UI;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Tests.RepositoryChoice;
using Avala.Workspaces.Contracts;
using Avalonia.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class NewRepositoryViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TypingANameAndPressingEnterCreatesTheRepositoryAsync() =>
        ui.RunAsync(async () =>
        {
            var created = new List<string>();
            var creation = await OpenedAsync(new FakeRepositories());
            creation.Created += (_, path) => created.Add(path);
            var view = Screen.Show(creation).Settle();

            view.Type("NewRepositoryName", "invoices").Press(Key.Enter);
            await (creation.CreateCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal("/home/ada/code/invoices", Assert.Single(created));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARefusedNameShowsWhyBesideTheFieldAsync() =>
        ui.RunAsync(async () =>
        {
            var creation = await OpenedAsync(new FakeRepositories { Refusal = WorkspaceFailure.InvalidRepositoryName });
            var view = Screen.Show(creation).Settle();

            view.Type("NewRepositoryName", "code/shop").Click("Create");
            await (creation.CreateCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal("Name the repository with a plain folder name, without slashes.", view.TextOf("CreationError"));
        }, TestContext.Current.CancellationToken);

    private static async Task<NewRepositoryViewModel> OpenedAsync(FakeRepositories repositories)
    {
        var creation = new NewRepositoryViewModel(new FakeFolderPicker { Answer = "/home/ada/code" }, repositories);
        await creation.StartCommand.ExecuteAsync(null);

        return creation;
    }
}
