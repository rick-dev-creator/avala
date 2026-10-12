using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Submitting;

namespace Avala.Workbench.Tests.RepositoryChoice;

public sealed class RepositoryPickerViewModelScripts
{
    private readonly FakeFolderPicker folders = new() { Answer = "/home/ada/Projects/notes" };

    [Fact]
    public void WithoutAnyJobYetNoRepositoryIsTheDefault()
    {
        var picker = Picker(new JobBoard());

        picker.ShowKnown();

        Assert.Equal((true, RepositoryPhrases.Scratch, string.Empty), (picker.IsScratch, picker.Title, picker.Chooser.Repository));
    }

    [Theory]
    [InlineData("/home/ada/code/shop-api", false, "shop-api", "/home/ada/code")]
    [InlineData("/data/scratch/2026-10-12-sketch-1", true, RepositoryPhrases.Scratch, "")]
    public void TheLastJobsRepositoryIsTheDefaultAndAScratchOneMeansNoRepository(string last, bool scratch, string title, string detail)
    {
        var picker = Picker(Pages.Board(Job("/home/ada/code/ledger", 1), Job(last, 2)));

        picker.ShowKnown();

        Assert.Equal((scratch, title, detail.Replace('/', Path.DirectorySeparatorChar)), (picker.IsScratch, picker.Title, picker.Detail));
    }

    [Fact]
    public void TheRecentRepositoriesAreListedWithTheCurrentOneMarkedAndChoosingOneUsesItAndClosesTheMenu()
    {
        var picker = Picker(Pages.Board(Job("/home/ada/code/ledger", 1), Job("/home/ada/code/shop-api", 2)));
        picker.ShowKnown();
        picker.IsOpen = true;
        var marked = picker.Recent.Select(choice => (choice.Title, choice.IsSelected)).ToList();

        picker.Recent[1].ChooseCommand.Execute(null);

        Assert.Equal([("shop-api", true), ("ledger", false)], marked);
        Assert.Equal(("/home/ada/code/ledger", false, false), (picker.Chooser.Repository, picker.IsScratch, picker.IsOpen));
        Assert.True(picker.Recent[1].IsSelected);
    }

    [Fact]
    public void ChoosingNoRepositoryKeepsTheChoiceWhenThePageLoadsAgain()
    {
        var picker = Picker(Pages.Board(Job("/home/ada/code/shop-api", 1)));
        picker.ShowKnown();

        picker.ChooseScratchCommand.Execute(null);
        picker.ShowKnown();

        Assert.Equal((true, string.Empty), (picker.IsScratch, picker.Chooser.Repository));
    }

    [Fact]
    public async Task OpenFolderAsksNearTheProjectsFolderAndUsesTheFolderChosenAsync()
    {
        var picker = Picker(new JobBoard());
        picker.ShowKnown();

        await picker.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal((RepositoryPhrases.ChooseTitle, "/home/ada/Projects"), Assert.Single(folders.Asked));
        Assert.Equal(("/home/ada/Projects/notes", "notes", false), (picker.Chooser.Repository, picker.Title, picker.IsScratch));
    }

    [Fact]
    public async Task CloneOpensTheCloneSheetAndClosesTheMenuAsync()
    {
        var picker = Picker(new JobBoard());
        picker.IsOpen = true;

        await picker.CloneCommand.ExecuteAsync(null);

        Assert.Equal((true, false), (picker.Field.Cloning.IsOpen, picker.IsOpen));
    }

    [Fact]
    public async Task ANewRepositoryArrivesInThePickerAsync()
    {
        var picker = Picker(new JobBoard());
        picker.ShowKnown();

        await picker.NewRepositoryCommand.ExecuteAsync(null);
        picker.Chooser.Creating.Name = "invoices";
        await picker.Chooser.Creating.CreateCommand.ExecuteAsync(null);

        Assert.Equal(("invoices", false), (picker.Title, picker.IsScratch));
    }

    private RepositoryPickerViewModel Picker(JobBoard board) =>
        new(Choosing.Field(board, folders, new FakeRepositories()), new RecentRepositories(board, Scratching.Folders()), new ProjectsPlace(new FakeProjects(), folders));

    private static JobSummary Job(string repository, int hours) =>
        Pages.Summary("Fix the failing test", JobStatus.Running) with { Repository = repository, Submitted = DateTimeOffset.UnixEpoch.AddHours(hours) };
}

public sealed class RepositoryChoiceViewModelScripts
{
    [Fact]
    public void ARepositoryIsShownByItsFolderNameBesideWhereItLives()
    {
        var chosen = new List<string>();
        var choice = new RepositoryChoiceViewModel("/home/ada/code/shop-api", chosen.Add);

        choice.ChooseCommand.Execute(null);

        Assert.Equal(("shop-api", "/home/ada/code".Replace('/', Path.DirectorySeparatorChar)), (choice.Title, choice.Detail));
        Assert.Equal(["/home/ada/code/shop-api"], chosen);
    }
}
