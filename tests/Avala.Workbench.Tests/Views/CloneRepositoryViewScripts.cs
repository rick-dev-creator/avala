using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Tests.RepositoryChoice;
using Avalonia.Controls;
using Avalonia.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class CloneRepositoryViewScripts(HeadlessUi ui)
{
    private const string Shop = "https://github.com/octo/shop.git";

    private static readonly string Beside = Path.GetDirectoryName("/home/ada/code/notes")!;

    [Fact]
    public Task CloneOpensBesideTheFieldAndPastingAnAddressThenEnterClonesIntoTheFieldAsync() =>
        ui.RunAsync(async () =>
        {
            var remotes = new FakeRemotes();
            var field = Choosing.Field(new JobBoard(), new FakeFolderPicker(), new FakeRepositories(), remotes);
            field.Repository = "/home/ada/code/notes";
            var view = Screen.Show(field).Settle();

            view.Click("CloneRepository");
            await (field.Cloning.StartCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            var opened = (view.Shows("CloneUrl"), view.TextOf("CloneParent"));
            view.Type("CloneUrl", Shop).Settle();
            var named = view.Find<TextBox>("CloneName").Text;
            view.Find<TextBox>("CloneUrl").Focus();
            view.Press(Key.Enter);
            await (field.Cloning.CloneCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((true, $"Into {Beside}", "shop"), (opened.Item1, opened.Item2, named));
            Assert.Equal(($"{Beside}/shop", false), (view.Find<ComboBox>("RepositoryPath").Text, view.Shows("CloneUrl")));
            Assert.Equal($"Cloned {Shop} into {Beside}/shop.", view.TextOf("Notice"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARunningCloneShowsItsProgressAndCancelStopsItAsync() =>
        ui.RunAsync(async () =>
        {
            var remotes = new FakeRemotes { Held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var field = Choosing.Field(new JobBoard(), new FakeFolderPicker(), new FakeRepositories(), remotes);
            field.Repository = "/home/ada/code/notes";
            await field.Cloning.StartCommand.ExecuteAsync(null);
            field.Cloning.Url = Shop;
            var view = Screen.Show(field).Settle();

            view.Click("Clone");
            await remotes.Started.Task;
            view.Settle();
            var running = (view.Shows("CloneProgress"), view.Find<TextBox>("CloneUrl").IsEnabled);
            view.Click("CancelClone");
            await (field.Cloning.CloneCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((true, false), running);
            Assert.Equal((false, "Clone cancelled: nothing was kept."), (view.Shows("CloneProgress"), view.TextOf("CloneError")));
        }, TestContext.Current.CancellationToken);
}
