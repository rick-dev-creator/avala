using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Tests.RepositoryChoice;
using Avala.Workspaces.Contracts;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class RepositoryFieldViewScripts(HeadlessUi ui)
{
    private const string Notes = "/home/ada/notes";

    [Fact]
    public Task ChooseFolderOpensThePickerAndPutsTheChosenFolderInTheFieldAsync() =>
        ui.RunAsync(async () =>
        {
            var picker = new FakeFolderPicker { Answer = Notes };
            var field = Choosing.Field(new Avala.Workbench.Board.JobBoard(), picker, new FakeRepositories());
            var view = Screen.Show(field).Settle();

            view.Click("Choose");
            await (field.ChooseCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Single(picker.Asked);
            Assert.Equal(Notes, view.Find<ComboBox>("RepositoryPath").Text);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheKnownRepositoriesAreListedInTheFieldsDropDownMostRecentFirstAsync() =>
        ui.RunAsync(() =>
        {
            var field = Choosing.Field(Pages.Board(Job("/code/shop-api", 1), Job("/code/shop-web", 2)));
            field.ShowKnown();
            var view = Screen.Show(field).Settle();

            view.Find<ComboBox>("RepositoryPath").IsDropDownOpen = true;
            view.Settle();

            Assert.Equal(
                ["/code/shop-web", "/code/shop-api"],
                view.Find<ComboBox>("RepositoryPath").GetRealizedContainers().OfType<ComboBoxItem>().Select(item => item.Content as string));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFolderThatIsNotARepositoryShowsWhyWithTheOfferThatMakesItOneAsync() =>
        ui.RunAsync(async () =>
        {
            var repositories = new FakeRepositories();
            repositories.States[Notes] = RepositoryState.NotARepository;
            var field = Choosing.Field(new Avala.Workbench.Board.JobBoard(), new FakeFolderPicker(), repositories);
            var view = Screen.Show(field).Settle();
            field.Repository = Notes;
            await field.Checking;
            view.Settle();
            var offered = (view.TextOf("ProblemText"), view.Shows("MakeRepository"));

            view.Click("MakeRepository");
            await (field.MakeRepositoryCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal(("This folder is not a git repository yet.", true), offered);
            Assert.Equal((false, true, Notes), (view.Shows("Problem"), view.Shows("Notice"), Assert.Single(repositories.Initialized)));
        }, TestContext.Current.CancellationToken);

    private static JobSummary Job(string repository, int hours) =>
        Pages.Summary("Fix the failing test", JobStatus.Running) with { Repository = repository, Submitted = DateTimeOffset.UnixEpoch.AddHours(hours) };
}
