using Avala.Jobs.Contracts;
using Avala.Workbench.RepositoryChoice;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.RepositoryChoice;

public sealed class RepositoryFieldViewModelScripts
{
    private const string Notes = "/home/ada/notes";

    private readonly FakeFolderPicker picker = new();
    private readonly FakeRepositories repositories = new();

    [Fact]
    public async Task AChosenFolderFillsTheFieldAndIsCheckedAtOnceAsync()
    {
        var field = Field();
        field.Repository = "/home/ada/code/shop";
        repositories.States[Notes] = RepositoryState.NotARepository;
        picker.Answer = Notes;

        await field.ChooseCommand.ExecuteAsync(null);

        Assert.Equal((RepositoryPhrases.ChooseTitle, "/home/ada/code/shop"), Assert.Single(picker.Asked));
        Assert.Equal(
            (Notes, "This folder is not a git repository yet.", RepositoryPhrases.MakeRepository, true),
            (field.Repository, field.Problem, field.Offer, field.MakeRepositoryCommand.CanExecute(null)));
    }

    [Fact]
    public async Task CancellingThePickerLeavesTheFieldAsItWasAsync()
    {
        var field = Field();
        field.Repository = "/home/ada/code/shop";

        await field.ChooseCommand.ExecuteAsync(null);

        Assert.Equal(("/home/ada/code/shop", string.Empty), (field.Repository, field.Problem));
    }

    [Fact]
    public void TheRepositoriesOfPastJobsAreSuggestedMostRecentFirstAndOnceEach()
    {
        var field = Choosing.Field(Pages.Board(Job("/code/old", 1), Job("/code/newest", 3), Job("/code/middle", 2), Job("/code/old", 0)));

        field.ShowKnown();

        Assert.Equal(["/code/newest", "/code/middle", "/code/old"], field.Repositories);
    }

    [Fact]
    public void ScratchRepositoriesOfJobsStartedWithoutARepositoryAreNeverSuggested()
    {
        var field = Choosing.Field(Pages.Board(Job("/code/shop", 1), Job("/data/scratch/2026-10-12-sketch-a-parser-a1b2c3", 2)));

        field.ShowKnown();

        Assert.Equal(["/code/shop"], field.Repositories);
    }

    [Fact]
    public async Task AFolderThatIsNotARepositoryIsMadeOneWhenThePersonAcceptsTheOfferAsync()
    {
        var field = Field();
        repositories.States[Notes] = RepositoryState.NotARepository;
        field.Repository = Notes;
        await field.Checking;

        await field.MakeRepositoryCommand.ExecuteAsync(null);

        Assert.Equal([Notes], repositories.Initialized);
        Assert.Equal((string.Empty, string.Empty, "notes is now a git repository with a first commit."), (field.Problem, field.Offer, field.Notice));
    }

    [Fact]
    public async Task ARepositoryWithoutACommitIsOfferedItsFirstCommitAsync()
    {
        var field = Field();
        repositories.States[Notes] = RepositoryState.NoCommit;

        field.Repository = Notes;
        await field.Checking;

        Assert.Equal(
            ("This repository has no commit yet, so a job has nothing to start from.", RepositoryPhrases.MakeFirstCommit),
            (field.Problem, field.Offer));
    }

    [Fact]
    public async Task WithoutAGitIdentityTheOfferSaysHowToSetOneAndStaysAsync()
    {
        var field = Field();
        repositories.States[Notes] = RepositoryState.NotARepository;
        repositories.Refusal = WorkspaceFailure.NoGitIdentity;
        field.Repository = Notes;
        await field.Checking;

        await field.MakeRepositoryCommand.ExecuteAsync(null);

        Assert.StartsWith("Git does not know who you are yet", field.Problem, StringComparison.Ordinal);
        Assert.Equal((RepositoryPhrases.MakeRepository, true), (field.Offer, field.MakeRepositoryCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ANewRepositoryIsNamedInAChosenParentAndFillsTheFieldAsync()
    {
        var field = Field();
        field.Repository = "/home/ada/code/shop";
        picker.Answer = "/home/ada/code";

        await field.Creation.StartCommand.ExecuteAsync(null);
        var opened = (field.Creation.IsOpen, field.Creation.Parent, picker.Asked[^1]);
        field.Creation.Name = "invoices";
        await field.Creation.CreateCommand.ExecuteAsync(null);

        Assert.Equal((true, "/home/ada/code", (RepositoryPhrases.ParentTitle, "/home/ada/code")), opened);
        Assert.Equal(
            ("/home/ada/code/invoices", false, string.Empty, "Created /home/ada/code/invoices: an empty git repository with a first commit."),
            (field.Repository, field.Creation.IsOpen, field.Creation.Name, field.Notice));
    }

    private RepositoryFieldViewModel Field() => Choosing.Field(new Avala.Workbench.Board.JobBoard(), picker, repositories);

    private static JobSummary Job(string repository, int hours) =>
        Pages.Summary("Fix the failing test", JobStatus.Running) with { Repository = repository, Submitted = DateTimeOffset.UnixEpoch.AddHours(hours) };
}
