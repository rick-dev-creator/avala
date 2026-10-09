using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.NewJob;
using Avala.Workbench.Submitting;

namespace Avala.Workbench.Tests.NewJob;

public sealed class NewJobViewModelScripts
{
    private readonly SubmittingJobs jobs = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheConnectionsOfTheMachineAreOfferedAfterTheRepositorysDefaultAndTheLatestRepositoryIsProposedAsync()
    {
        var page = Page(Pages.Summary("Fix the failing test", JobStatus.Running));

        await page.LoadAsync(Cancellation);

        Assert.Equal([NewJobViewModel.RepositoryDefault, "work", "personal"], page.Connections);
        Assert.Equal((NewJobViewModel.RepositoryDefault, "/repositories/shop"), (page.Connection, page.Repository));
    }

    [Fact]
    public async Task ASubmittedJobRunsOnTheChosenConnectionSupervisedAndTheInstructionIsClearedAsync()
    {
        var page = Page();
        await page.LoadAsync(Cancellation);
        page.Repository = " /repositories/shop ";
        page.Instruction = " Add an endpoint ";
        page.Connection = "personal";
        page.Supervised = true;

        await page.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(jobs.Requests);
        Assert.Equal(
            ("/repositories/shop", "Add an endpoint", Option<ConnectionName>.Some(new ConnectionName("personal")), Option<Autonomy>.Some(Autonomy.Supervised)),
            (request.RepositoryPath, request.Instruction, request.Connection, request.Autonomy));
        Assert.Equal((string.Empty, "Submitted: Add an endpoint", string.Empty, true), (page.Instruction, page.Submitted, page.Error, page.LastSubmitted.IsSome));
    }

    [Fact]
    public async Task TheRepositorysDefaultsApplyUnlessAConnectionOrSupervisionIsChosenAsync()
    {
        var page = Page();
        page.Repository = "/repositories/shop";
        page.Instruction = "Add an endpoint";

        await page.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(jobs.Requests);
        Assert.Equal((true, true), (request.Connection.IsNone, request.Autonomy.IsNone));
    }

    [Fact]
    public async Task ARejectedJobKeepsItsInstructionAndShowsWhyAsync()
    {
        var page = Page();
        jobs.Refusal = JobRejection.UnknownConnection;
        page.Repository = "/repositories/shop";
        page.Instruction = "Add an endpoint";

        await page.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(("Add an endpoint", "No connection has that name.", false), (page.Instruction, page.Error, page.LastSubmitted.IsSome));
    }

    [Theory]
    [InlineData("", "Add an endpoint")]
    [InlineData("/repositories/shop", " ")]
    public void AJobNeedsARepositoryAndAnInstruction(string repository, string instruction)
    {
        var page = Page();
        page.Repository = repository;
        page.Instruction = instruction;

        Assert.False(page.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReloadingKeepsAChosenConnectionStillOfferedAndATypedRepository()
    {
        var page = Page(Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running));
        await page.LoadAsync(Cancellation);
        page.Connection = "personal";
        page.Repository = "/repositories/web";

        await page.LoadAsync(Cancellation);

        Assert.Equal(("personal", "/repositories/web"), (page.Connection, page.Repository));
    }

    [Fact]
    public async Task AChosenConnectionNoLongerOfferedFallsBackToTheRepositorysDefault()
    {
        var page = Page();
        page.Connection = "retired";

        await page.LoadAsync(Cancellation);

        Assert.Equal(NewJobViewModel.RepositoryDefault, page.Connection);
    }

    [Fact]
    public async Task ActivatingThePageLoadsItsChoices()
    {
        var page = Page();

        page.Activate();
        await page.Loading;

        Assert.Equal(("New job", 3), (page.Title, page.Connections.Count));
    }

    private NewJobViewModel Page(params JobSummary[] known) =>
        new(new JobLaunch(jobs, new FakeConnections("work", "personal")), Pages.Board(known));
}
