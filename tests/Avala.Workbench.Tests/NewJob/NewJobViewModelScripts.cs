using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.NewJob;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.NewJob;

public sealed class NewJobViewModelScripts
{
    private readonly SubmittingJobs jobs = new();
    private readonly FakeConnections connections = new FakeConnections("work", "personal").Automatic();
    private readonly FakePreview preview = new();
    private readonly FakePolicies policies = new();
    private readonly IMessenger messenger = new StrongReferenceMessenger();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheConnectionsOfTheMachineAreOfferedAfterAutoAndTheLatestRepositoryIsProposedAsync()
    {
        var page = Page(Pages.Summary("Fix the failing test", JobStatus.Running));

        await page.LoadAsync(Cancellation);

        Assert.Equal([NewJobPhrases.Auto, "work", "personal"], page.Connections);
        Assert.Equal((NewJobPhrases.Auto, "/repositories/shop"), (page.Connection, page.Repository));
        Assert.Equal("/repositories/shop", preview.Asked[^1]);
    }

    [Fact]
    public async Task AutoSaysWhichConnectionItWouldPickNowAndWhyAsync()
    {
        preview.Answer = FakePreview.ByCapacity("personal", ChoiceReason.MostCapacity, ("work", 0.88, true), ("personal", 0.41, true));
        var page = Page();

        await page.LoadAsync(Cancellation);

        Assert.Equal(("Auto → personal · 41% of the 5-hour window used · the most capacity left", false), (page.Route, page.IsRouteAttention));
    }

    [Fact]
    public async Task WithoutAnyReadingAutoSaysThereIsNoCapacityToCompareInsteadOfClaimingTheMostCapacityAsync()
    {
        preview.Answer = FakePreview.ByCapacity("work", ChoiceReason.MostCapacity, ("work", 0, true), ("personal", 0, true));
        var page = Page();

        await page.LoadAsync(Cancellation);

        Assert.Equal(
            ("Auto → work · no connection has reported usage yet, so there is no capacity to compare: the first usable connection", false),
            (page.Route, page.IsRouteAttention));
    }

    [Fact]
    public async Task WhenEveryConnectionIsAtItsLimitAutoWarnsThatTheBudgetWillHoldTheJobAsync()
    {
        preview.Answer = FakePreview.ByCapacity("personal", ChoiceReason.AllAtLimit, ("work", 0.97, false), ("personal", 0.93, false));
        var page = Page();

        await page.LoadAsync(Cancellation);

        Assert.Equal(
            ("Auto → personal · all at their limit, least used: 93% of the 5-hour window used · the budget will hold the job", true),
            (page.Route, page.IsRouteAttention));
    }

    [Fact]
    public async Task AFixedMachineDefaultIsOfferedFirstAndExplainedAsync()
    {
        var fixedDefault = new FakeConnections("work", "personal");
        preview.Answer = new ConnectionPreview(ConnectionRoute.MachineDefault, new ConnectionName("work"));
        var page = Page(fixedDefault);

        await page.LoadAsync(Cancellation);

        Assert.Equal(["Default (work)", "work", "personal"], page.Connections);
        Assert.Equal(("Default (work)", "Default → work · the default connection of this machine"), (page.Connection, page.Route));
    }

    [Fact]
    public async Task AConnectionTheRepositoryNamesIsWhatAutoWouldRunOnAsync()
    {
        preview.Answer = new ConnectionPreview(ConnectionRoute.Repository, new ConnectionName("personal"));
        var page = Page();

        await page.LoadAsync(Cancellation);

        Assert.Equal("Auto → personal · named by the repository's .avala/jobs.json", page.Route);
    }

    [Fact]
    public async Task WithoutAnyConnectionAutoSaysNoJobCanRunAsync()
    {
        var page = Page(new FakeConnections().Automatic());

        await page.LoadAsync(Cancellation);

        Assert.Equal([NewJobPhrases.Auto], page.Connections);
        Assert.Equal((NewJobPhrases.Unavailable, true), (page.Route, page.IsRouteAttention));
    }

    [Fact]
    public async Task ARejectedConnectionsFileSaysWhereToRepairItAsync()
    {
        connections.Catalog = new ConnectionCatalog(ConnectionFileStatus.Rejected, ConnectionError.UnknownDefault, [], Option<ConnectionName>.None);
        var page = Page();

        await page.LoadAsync(Cancellation);

        Assert.Equal(NewJobPhrases.Auto, Assert.Single(page.Connections));
        Assert.Equal(
            ("connections.json is rejected (UnknownDefault), so no job can start: choose the default connection again in Settings.", true),
            (page.Route, page.IsRouteAttention));
    }

    [Fact]
    public async Task AnInvalidJobFileInTheRepositoryIsSaidBeforeSubmittingAsync()
    {
        preview.Answer = JobRejection.InvalidJobFile;
        var page = Page();

        await page.LoadAsync(Cancellation);

        Assert.Equal(("The repository's .avala/jobs.json is invalid: the job would fail before it starts.", true), (page.Route, page.IsRouteAttention));
    }

    [Fact]
    public async Task AConnectionChosenNearItsLimitIsSubmittedAsChosenAndSaysItRunsThereAnywayAsync()
    {
        preview.Answer = FakePreview.ByCapacity("personal", ChoiceReason.MostCapacity, ("work", 0.95, false), ("personal", 0.2, true));
        var page = Page();
        await page.LoadAsync(Cancellation);
        page.Instruction = "Add an endpoint";

        page.Connection = "work";
        await page.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            ("Runs on work · 95% of the 5-hour window used · at its limit, the budget may hold the job", true),
            (page.Route, page.IsRouteAttention));
        Assert.Equal(Option<ConnectionName>.Some(new ConnectionName("work")), Assert.Single(jobs.Requests).Connection);
    }

    [Fact]
    public async Task ASubmittedJobRunsOnTheChosenConnectionSupervisedAndTheInstructionIsClearedAsync()
    {
        var page = Page();
        await page.LoadAsync(Cancellation);
        page.Repository = " /repositories/shop ";
        page.Instruction = " Add an endpoint ";
        page.Connection = "personal";
        page.Autonomy = NewJobPhrases.Supervised;

        await page.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(jobs.Requests);
        Assert.Equal(
            ("/repositories/shop", "Add an endpoint", Option<ConnectionName>.Some(new ConnectionName("personal")), Option<Autonomy>.Some(Autonomy.Supervised)),
            (request.RepositoryPath, request.Instruction, request.Connection, request.Autonomy));
        Assert.Equal((string.Empty, "Submitted: Add an endpoint", string.Empty, true), (page.Instruction, page.Submitted, page.Error, page.LastSubmitted.IsSome));
    }

    [Fact]
    public async Task AutoNamesNoConnectionSoTheDefaultsApplyUnlessSupervisionIsChosenAsync()
    {
        var page = Page();
        await page.LoadAsync(Cancellation);
        page.Instruction = "Add an endpoint";

        await page.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(jobs.Requests);
        Assert.Equal((true, true), (request.Connection.IsNone, request.Autonomy.IsNone));
    }

    [Fact]
    public async Task TheAutonomyShowsTheRepositorysLevelAndOffersSupervisedOnlyWhenItTightensItAsync()
    {
        var page = Page(Pages.Summary("Fix the failing test", JobStatus.Running));

        await page.LoadAsync(Cancellation);
        var autonomous = (string.Join(" | ", page.Autonomies), page.Autonomy, page.AutonomyNote);
        page.Autonomy = NewJobPhrases.Supervised;
        var tightened = page.AutonomyNote;
        policies.Policy = FakePolicies.Declaring(Autonomy.Supervised);
        page.Repository = "/repositories/other";
        await page.Previewing;

        Assert.Equal(
            ("Repository's level: autonomous | Supervised", "Repository's level: autonomous", "Autonomous, as the repository's .avala/permissions.json declares: edits and commands inside the worktree run without asking, anything else is denied, forms are answered by policy."),
            autonomous);
        Assert.Equal("Supervised for this job only: whatever the rules leave open asks you first.", tightened);
        Assert.Equal(["Repository's level: supervised"], page.Autonomies);
        Assert.Equal(
            ("Repository's level: supervised", "Supervised, as the repository declares: whatever the rules leave open asks you first. A job can tighten its autonomy, never loosen it."),
            (page.Autonomy, page.AutonomyNote));
        Assert.Equal(("/repositories/shop", "/repositories/other"), (policies.Asked[0], policies.Asked[^1]));
    }

    [Fact]
    public async Task ARejectedPermissionsFileSaysTheJobRunsSupervisedUnderTheBuiltInRulesAsync()
    {
        policies.Policy = new Avala.Permissions.Contracts.RepositoryPolicy(
            Avala.Permissions.Contracts.PolicyFileStatus.Rejected,
            Avala.Permissions.Contracts.PolicyError.Malformed,
            [],
            Option<Avala.Workspaces.Contracts.FileOrigin>.None);
        var page = Page(Pages.Summary("Fix the failing test", JobStatus.Running));

        await page.LoadAsync(Cancellation);

        Assert.Equal(["Repository's level: supervised"], page.Autonomies);
        Assert.Equal(
            "Supervised: the repository's .avala/permissions.json is rejected (Malformed), so the built-in rules apply and whatever they leave open asks you.",
            page.AutonomyNote);
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
    public async Task ReloadingKeepsAChosenConnectionStillOfferedAndATypedRepositoryAsync()
    {
        var page = Page(Pages.Summary("Fix JPY rounding in invoice totals", JobStatus.Running));
        await page.LoadAsync(Cancellation);
        page.Connection = "personal";
        page.Repository = "/repositories/web";

        await page.LoadAsync(Cancellation);

        Assert.Equal(("personal", "/repositories/web"), (page.Connection, page.Repository));
    }

    [Fact]
    public async Task AChosenConnectionThatDisappearedFallsBackToAutoAsync()
    {
        var page = Page();
        await page.LoadAsync(Cancellation);
        page.Connection = "personal";
        connections.Catalog = connections.Catalog with { Connections = [connections.Catalog.Connections[0]] };

        await page.LoadAsync(Cancellation);

        Assert.Equal([NewJobPhrases.Auto, "work"], page.Connections);
        Assert.Equal(NewJobPhrases.Auto, page.Connection);
    }

    [Fact]
    public async Task SwitchingTheDefaultWhileThePageIsOpenRelabelsItsFirstChoiceAndExplainsAgainAsync()
    {
        var page = Page();
        page.Activate();
        await page.Loading;
        var before = page.Connection;
        connections.Catalog = connections.Catalog with { Default = new ConnectionName("personal"), DefaultMode = DefaultMode.Fixed };
        preview.Answer = new ConnectionPreview(ConnectionRoute.MachineDefault, new ConnectionName("personal"));

        messenger.Send(new DefaultConnectionChanged(DefaultMode.Fixed, new ConnectionName("personal")));
        await page.Loading;

        Assert.Equal(NewJobPhrases.Auto, before);
        Assert.Equal(("Default (personal)", "Default → personal · the default connection of this machine"), (page.Connection, page.Route));
    }

    [Fact]
    public async Task SwitchingTheDefaultKeepsAConnectionChosenByHandAsync()
    {
        var page = Page();
        page.Activate();
        await page.Loading;
        page.Connection = "work";
        connections.Catalog = connections.Catalog with { Default = new ConnectionName("personal"), DefaultMode = DefaultMode.Fixed };

        messenger.Send(new DefaultConnectionChanged(DefaultMode.Fixed, new ConnectionName("personal")));
        await page.Loading;

        Assert.Equal(("Default (personal)", "work"), (page.Connections[0], page.Connection));
    }

    [Fact]
    public async Task ALeavingPageNoLongerFollowsTheDefaultAsync()
    {
        var page = Page();
        page.Activate();
        await page.Loading;
        page.Deactivate();
        var asked = preview.Asked.Count;

        messenger.Send(new DefaultConnectionChanged(DefaultMode.Auto, Option<ConnectionName>.None));

        Assert.Equal(asked, preview.Asked.Count);
    }

    [Fact]
    public async Task ALatePreviewOfAnEarlierRepositoryNeverReplacesTheLatestAsync()
    {
        var late = new TaskCompletionSource<Result<ConnectionPreview, JobRejection>>(TaskCreationOptions.RunContinuationsAsynchronously);
        preview.Pending["/repositories/old"] = late;
        preview.Answer = new ConnectionPreview(ConnectionRoute.Repository, new ConnectionName("work"));
        var page = Page();
        await page.LoadAsync(Cancellation);

        page.Repository = "/repositories/old";
        var earlier = page.Previewing;
        page.Repository = "/repositories/new";
        await page.Previewing;
        late.SetResult(new ConnectionPreview(ConnectionRoute.Repository, new ConnectionName("personal")));
        await earlier;

        Assert.Equal("Auto → work · named by the repository's .avala/jobs.json", page.Route);
    }

    [Fact]
    public async Task ActivatingThePageLoadsItsChoicesAsync()
    {
        var page = Page();

        page.Activate();
        await page.Loading;

        Assert.Equal(("New job", 3), (page.Title, page.Connections.Count));
    }

    private NewJobViewModel Page(params JobSummary[] known) => Page(connections, known);

    private NewJobViewModel Page(FakeConnections machine, params JobSummary[] known) =>
        new(new JobLaunch(jobs, machine, preview, policies), Pages.Board(known), messenger);
}
