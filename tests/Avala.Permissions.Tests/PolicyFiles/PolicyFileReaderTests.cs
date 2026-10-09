using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.PolicyFiles;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests.PolicyFiles;

public sealed class PolicyFileReaderTests
{
    private const string Worktree = "/worktrees/1";
    private const string PolicyFile = ".avala/permissions.json";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABaseCommitWithoutAPolicyFileHasNoRepositoryRulesAsync()
    {
        var file = await new PolicyFileReader(new CommittedFiles().Workspace(Worktree)).ReadAsync(Worktree, Cancellation);

        Assert.True(Outcomes.Succeeds(file.Policy).IsNone);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin()), file.Origin);
    }

    [Fact]
    public async Task ThePolicyFileOfTheBaseCommitIsParsedWithItsOriginAsync()
    {
        var committed = new CommittedFiles().With(
            Worktree,
            PolicyFile,
            """{ "rules": [ { "name": "tests", "kind": "command", "target": "dotnet test", "answer": "allow" } ] }""",
            editedInWorktree: true);

        var file = await new PolicyFileReader(committed).ReadAsync(Worktree, Cancellation);

        Assert.Equal("tests", Assert.Single(Outcomes.Present(Outcomes.Succeeds(file.Policy)).Repository).Name);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin(editedInWorktree: true)), file.Origin);
        Assert.Equal([(Worktree, PolicyFile)], committed.Reads);
    }

    [Fact]
    public async Task APolicyFileOverTheSizeLimitIsRejectedUnparsedAsync()
    {
        var committed = new CommittedFiles().With(Worktree, PolicyFile, $$"""{ "rules": [], "padding": "{{new string(' ', PolicyFileReader.MaximumBytes)}}" }""");

        Assert.Equal(PolicyError.TooLarge, Outcomes.FailsWith((await new PolicyFileReader(committed).ReadAsync(Worktree, Cancellation)).Policy));
    }

    [Fact]
    public async Task AFolderThatIsNoJobWorkspaceHasNoRepositoryRulesAndNoOriginAsync()
    {
        var file = await new PolicyFileReader(new CommittedFiles()).ReadAsync("/elsewhere", Cancellation);

        Assert.True(Outcomes.Succeeds(file.Policy).IsNone);
        Assert.True(file.Origin.IsNone);
    }

    [Fact]
    public async Task ABaseCommitThatCannotBeReadIsUnreadableAsync()
    {
        var file = await new PolicyFileReader(new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed)).ReadAsync(Worktree, Cancellation);

        Assert.Equal(PolicyError.Unreadable, Outcomes.FailsWith(file.Policy));
    }

    [Fact]
    public async Task ARepositorysCurrentPolicyListsItsRulesInDecisionOrderWithTheirOriginsAsync()
    {
        var committed = new CommittedFiles().With(
            Repository,
            PolicyFile,
            """{ "autonomy": "autonomous", "formAnswers": "bestJudgment", "rules": [ { "name": "tests", "kind": "command", "target": "dotnet test", "answer": "allow" } ] }""");

        var policy = await new PolicyFileReader(committed).OfRepositoryAsync(Repository, Cancellation);

        Assert.Equal(
            (PolicyFileStatus.Applied, Option<PolicyError>.None, Autonomy.Autonomous, FormStrategy.BestJudgment, Option<FileOrigin>.Some(CommittedFiles.Origin())),
            (policy.File, policy.Error, policy.Autonomy, policy.Strategy, policy.Origin));
        Assert.Equal(
            [
                (RuleOrigin.BuiltIn, "policy-file-goes-to-a-human"),
                (RuleOrigin.BuiltIn, "edits-outside-the-workspace-go-to-a-human"),
                (RuleOrigin.Repository, "tests"),
                (RuleOrigin.BuiltIn, "edits-inside-the-workspace"),
                (RuleOrigin.BuiltIn, "autonomous-commands-in-the-worktree"),
                (RuleOrigin.BuiltIn, "autonomous-denies-the-rest"),
            ],
            policy.Rules.Select(rule => (rule.Origin, rule.Name)));
        Assert.Equal([(Repository, PolicyFile)], committed.Reads);
    }

    [Theory]
    [InlineData(false, nameof(PolicyFileStatus.Absent), null)]
    [InlineData(true, nameof(PolicyFileStatus.Rejected), nameof(PolicyError.Malformed))]
    public async Task ARepositoryWithoutAValidPolicyKeepsTheBuiltInRulesAndSaysWhyAsync(bool declared, string status, string? error)
    {
        var committed = declared ? new CommittedFiles().With(Repository, PolicyFile, "not json") : new CommittedFiles().Workspace(Repository);

        var policy = await new PolicyFileReader(committed).OfRepositoryAsync(Repository, Cancellation);

        Assert.Equal(
            (Enum.Parse<PolicyFileStatus>(status), error is null ? Option<PolicyError>.None : Enum.Parse<PolicyError>(error), Autonomy.Supervised),
            (policy.File, policy.Error, policy.Autonomy));
        Assert.Equal(
            ["policy-file-goes-to-a-human", "edits-outside-the-workspace-go-to-a-human", "edits-inside-the-workspace"],
            policy.Rules.Select(rule => rule.Name));
    }

    [Fact]
    public async Task AFolderThatIsNoRepositoryHasAnUnreadablePolicyWithoutOriginAsync()
    {
        var policy = await new PolicyFileReader(new CommittedFiles().Failing(Repository, WorkspaceFailure.NotAGitRepository)).OfRepositoryAsync(Repository, Cancellation);

        Assert.Equal(
            (PolicyFileStatus.Rejected, Option<PolicyError>.Some(PolicyError.Unreadable), Option<FileOrigin>.None),
            (policy.File, policy.Error, policy.Origin));
    }

    private const string Repository = "/repositories/shop";
}
