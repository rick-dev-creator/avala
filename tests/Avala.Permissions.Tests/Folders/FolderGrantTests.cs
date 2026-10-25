using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Folders;
using Avala.Permissions.Governance;
using Avala.Permissions.Links;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Permissions.Tests.Folders;

public sealed class FolderGrantTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryFolder worktree = new();
    private readonly TemporaryFolder other = new();
    private readonly TemporaryFolder projects = new();
    private readonly GovernanceBook book = new(new Governance.InMemoryGovernance());
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly SessionId session = SessionId.New();
    private readonly JobId job = JobId.New();
    private readonly FakeTimeProvider clock = new(Now);
    private readonly SessionGovernor governor;
    private readonly FolderDesk desk;
    private readonly HumanAnswers answers;

    public FolderGrantTests()
    {
        var links = new SymbolicLinks();
        var readable = new FixedReadableFolders([projects.Path]);
        governor = new SessionGovernor(book, new SessionTerms(new NoPolicyFiles(), []), new PermissionResponder(agents, new RequestPlaces(links, readable), clock, []), bus);
        desk = new FolderDesk(agents, new FolderCoverage(book, links, readable, clock), new FolderAsking(book, clock, bus));
        answers = new HumanAnswers(book, agents, new RepositoryFiles(), new AnswerLedger(book, bus, clock, new RecordedRounds()));
        Directory.CreateDirectory(Path.Combine(other.Path, ".git"));
        Govern(Autonomy.Supervised);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Folder => new SymbolicLinks().Resolve(other.Path).Match(real => real, () => other.Path);

    public void Dispose()
    {
        worktree.Dispose();
        other.Dispose();
        projects.Dispose();
    }

    [Fact]
    public async Task AFolderRequestWaitsForAPersonEvenWhenTheJobRunsOnItsOwnAsync()
    {
        Govern(Autonomy.Autonomous);

        await CallAsync("ask", other.Path, "edit", "Bump the shared contracts package");

        var decision = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal(
            (DecisionDelivery.LeftToHuman, PolicyAnswer.Ask, Folder, Option<FolderRequest>.Some(new FolderRequest(Folder, FolderAccess.Edit, "Bump the shared contracts package"))),
            (decision.Delivery, decision.Answer, decision.Target, decision.Folder));
        Assert.Empty(agents.Returns);
    }

    [Fact]
    public async Task AFolderRequestThatArrivesBeforeItsSessionIsGivenItsJobWaitsForTheJobAsync()
    {
        var early = SessionId.New();
        book.Keep(book.Of(early).OpenedIn(worktree.Path, PermissionPolicy.BuiltIn, Report(early)));

        var called = CallAsync(early, "ask", other.Path, "edit", "Update the client");
        book.Keep(book.Of(early).WorkingOn(job));
        await called;

        var decision = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((Option<JobId>.Some(job), Folder), (decision.Job, decision.Target));
        Assert.Empty(agents.Returns);
    }

    [Fact]
    public async Task AFolderRequestFromASessionThatNeverRunsAJobIsRefusedAsync()
    {
        var alone = SessionId.New();
        book.Keep(book.Of(alone).OpenedIn(worktree.Path, PermissionPolicy.BuiltIn, Report(alone)));

        var called = CallAsync(alone, "ask", other.Path, "edit", "Update the client");
        clock.Advance(TimeSpan.FromMinutes(1));
        await called;

        Assert.Equal("Refused: this session runs no job, so there is nothing to grant the folder for.", Assert.Single(agents.Returns).Item2.Content);
    }

    [Fact]
    public async Task ReadAndEditGrantsTheFolderForTheRestOfTheJobAndTellsTheAgentAsync()
    {
        Govern(Autonomy.Autonomous);
        await CallAsync("ask", other.Path, "edit", "Update the client");

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, Grant("ask", Remember.EditsInThisFolder), Cancellation));
        var edit = await EditAsync("edit", Path.Combine(other.Path, "src", "client.cs"));

        Assert.Equal(Option<PolicyRule>.Some(Remembering.Granted(Folder, FolderAccess.Edit)), answer.Rule);
        Assert.StartsWith("Granted: you may read and edit files in", Assert.Single(agents.Returns).Item2.Content, StringComparison.Ordinal);
        Assert.Equal((PolicyAnswer.Allow, Option<string>.Some(Remembering.EditsInFolder)), (edit.Answer, edit.Rule.Map(rule => rule.Name)));
    }

    [Fact]
    public async Task AGrantedFolderStillSendsItsGitFolderAndPolicyFileToAPersonAsync()
    {
        await CallAsync("ask", other.Path, "edit", "Update the client");
        Outcomes.Succeeds(await answers.AnswerAsync(session, Grant("ask", Remember.EditsInThisFolder), Cancellation));

        var git = await EditAsync("git", Path.Combine(other.Path, ".git", "config"));
        var policy = await EditAsync("policy", Path.Combine(other.Path, ".avala", "permissions.json"));

        Assert.Equal(
            [(PolicyAnswer.Ask, "edits-outside-the-workspace-go-to-a-human"), (PolicyAnswer.Ask, "edits-outside-the-workspace-go-to-a-human")],
            new[] { git, policy }.Select(decision => (decision.Answer, Outcomes.Present(decision.Rule).Name)));
    }

    [Fact]
    public async Task ReadOnlyGrantsReadsButEditsThereStillAskAsync()
    {
        await CallAsync("ask", other.Path, "edit", "Update the client");

        Outcomes.Succeeds(await answers.AnswerAsync(session, Grant("ask", Remember.ReadsInThisFolder), Cancellation));
        var read = await ReadAsync("read", Path.Combine(other.Path, "README.md"));
        var edit = await EditAsync("edit", Path.Combine(other.Path, "README.md"));

        Assert.StartsWith("Granted read only", Assert.Single(agents.Returns).Item2.Content, StringComparison.Ordinal);
        Assert.Equal((PolicyAnswer.Allow, PolicyAnswer.Ask), (read.Answer, edit.Answer));
    }

    [Fact]
    public async Task ARefusedFolderTellsTheAgentAndGrantsNothingAsync()
    {
        await CallAsync("ask", other.Path, "read", "Read the schema");

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, new PermissionReply(new ItemId("ask"), PermissionAnswer.Deny) { Message = "Use the copy in docs/" }, Cancellation));

        Assert.Equal(Option<PolicyRule>.None, answer.Rule);
        Assert.Empty(book.JobRulesOf(job));
        Assert.Contains("Use the copy in docs/", Assert.Single(agents.Returns).Item2.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFolderRequestNeedsAnAccessChosenWhenAllowedAsync()
    {
        await CallAsync("ask", other.Path, "edit", "Update the client");

        Assert.Equal(PolicyError.ScopeNotOffered, Outcomes.FailsWith(await answers.AnswerAsync(session, Grant("ask", Remember.Once), Cancellation)));
    }

    [Fact]
    public async Task AFolderAlreadyGrantedOrReadableIsAnsweredAtOnceAsync()
    {
        await CallAsync("ask", other.Path, "edit", "Update the client");
        Outcomes.Succeeds(await answers.AnswerAsync(session, Grant("ask", Remember.EditsInThisFolder), Cancellation));
        Directory.CreateDirectory(Path.Combine(projects.Path, "docs"));
        Directory.CreateDirectory(Path.Combine(other.Path, "src"));

        await CallAsync("again", Path.Combine(other.Path, "src"), "edit", "Again");
        await CallAsync("projects", Path.Combine(projects.Path, "docs"), "read", "Read the docs");

        Assert.Equal([true, true], agents.Returns.Skip(1).Select(returned => returned.Item2.Content.StartsWith("Already granted", StringComparison.Ordinal)));
        Assert.Single(bus.Published.OfType<PermissionDecided>());
    }

    [Fact]
    public async Task AFolderRequestThatCannotBeGrantedIsRefusedAtOnceAsync()
    {
        await CallAsync("malformed", other.Path, "write", "Nope");
        await CallAsync("missing", Path.Combine(other.Path, "absent"), "read", "Nope");
        await CallAsync("mine", Path.Combine(worktree.Path, "src"), "read", "Nope");

        Assert.Equal(
            [true, true, false],
            agents.Returns.Select(returned => returned.Item2.IsError));
        Assert.Contains("inside your worktree", agents.Returns[^1].Item2.Content, StringComparison.Ordinal);
        Assert.Empty(bus.Published.OfType<PermissionDecided>());
    }

    [Fact]
    public async Task AnEditOutsideTheWorktreeOffersItsRepositoryForTheRestOfTheJobAsync()
    {
        var asked = await EditAsync("edit", Path.Combine(other.Path, "src", "client.cs"));

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, new PermissionReply(new ItemId("edit"), PermissionAnswer.Allow) { Remember = Remember.EditsInThisFolder }, Cancellation));
        var later = await EditAsync("later", Path.Combine(other.Path, "README.md"));

        Assert.Equal(Option<string>.Some(Folder), asked.Scope.Bind(scope => scope.Folder));
        Assert.Equal(Option<PolicyRule>.Some(Remembering.Granted(Folder, FolderAccess.Edit)), answer.Rule);
        Assert.Equal([PermissionAnswer.Allow, PermissionAnswer.Allow], agents.Responses.Select(response => response.Item2.Answer));
        Assert.Equal(PolicyAnswer.Allow, later.Answer);
    }

    [Fact]
    public async Task AnOutsideEditDeniedWhileTheJobRanOnItsOwnCanBeAllowedAsItsRepositoryForTheRestOfTheJobAsync()
    {
        Govern(Autonomy.Autonomous);
        var denied = await EditAsync("edit", Path.Combine(other.Path, "src", "client.cs"));

        var answer = Outcomes.Succeeds(await new DeniedRequests(book, new AnswerLedger(book, bus, new FakeTimeProvider(Now), new RecordedRounds())).AllowAsync(session, new ItemId("edit"), Cancellation));
        var retried = await EditAsync("retry", Path.Combine(other.Path, "src", "client.cs"));

        Assert.Equal(PolicyAnswer.Deny, denied.Answer);
        Assert.Equal(Option<PolicyRule>.Some(Remembering.Granted(Folder, FolderAccess.Edit)), answer.Rule);
        Assert.Equal(PolicyAnswer.Allow, retried.Answer);
    }

    [Fact]
    public async Task AnEditIntoAGitFolderOffersNoRepositoryAsync()
    {
        var asked = await EditAsync("git", Path.Combine(other.Path, ".git", "config"));

        Assert.Equal(Option<string>.None, asked.Scope.Bind(scope => scope.Folder));
    }

    [Fact]
    public async Task AGrantNeverReachesAnotherJobAsync()
    {
        await CallAsync("ask", other.Path, "edit", "Update the client");
        Outcomes.Succeeds(await answers.AnswerAsync(session, Grant("ask", Remember.EditsInThisFolder), Cancellation));
        var stranger = SessionId.New();
        book.Keep(book.Of(stranger).OpenedIn(worktree.Path, PermissionPolicy.BuiltIn, Report(stranger)).WorkingOn(JobId.New()));

        await governor.HandleAsync(new AgentActivity(new PermissionRequested(stranger, TurnId.New(), new ItemId("edit"), "Edit", ItemKind.FileEdit, Path.Combine(other.Path, "README.md"))), Cancellation);

        Assert.Equal(PolicyAnswer.Ask, Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision.Answer);
    }

    private void Govern(Autonomy autonomy) =>
        book.Keep(book.Of(session).OpenedIn(worktree.Path, PermissionPolicy.BuiltIn with { Declared = autonomy }, Report(session)).WorkingOn(job));

    private static SessionPolicy Report(SessionId reported) => new(reported, PolicyFileStatus.Applied, Option<PolicyError>.None, [], CommittedFiles.Origin());

    private static PermissionReply Grant(string item, Remember remember) => new(new ItemId(item), PermissionAnswer.Allow) { Remember = remember };

    private Task CallAsync(string item, string path, string access, string reason) => CallAsync(session, item, path, access, reason);

    private Task CallAsync(SessionId caller, string item, string path, string access, string reason) =>
        desk.HandleAsync(
            new AgentActivity(new ToolCalled(caller, TurnId.New(), new ItemId(item), FolderTool.Name, $$"""{"path":"{{path}}","access":"{{access}}","reason":"{{reason}}"}""")),
            Cancellation).AsTask();

    private async Task<PolicyDecision> EditAsync(string item, string path)
    {
        await governor.HandleAsync(new AgentActivity(new PermissionRequested(session, TurnId.New(), new ItemId(item), "Edit", ItemKind.FileEdit, path)), Cancellation);

        return Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
    }

    private async Task<PolicyDecision> ReadAsync(string item, string path)
    {
        await governor.HandleAsync(new AgentActivity(new PermissionRequested(session, TurnId.New(), new ItemId(item), "Read", ItemKind.Other, path) { Reads = path }), Cancellation);

        return Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
    }
}
