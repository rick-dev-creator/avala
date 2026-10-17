using Avala.Documentation.Contracts;
using Avala.Documentation.RepositoryFiles;
using Avala.Documentation.Tests.Writing;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Tests.RepositoryFiles;

public sealed class DocsRulesTests
{
    private const string Worktree = "/worktrees/job-1";

    private const string Repository = "/repos/ledger";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{ "docs": { "enabled": true } }""", true, "docs/features")]
    [InlineData("""{ "docs": { "enabled": true, "folder": "documentation/features/" } }""", true, "documentation/features")]
    [InlineData("""{ "docs": { "enabled": false, "folder": "notes" } }""", false, "notes")]
    [InlineData("""{ "docs": {} }""", false, "docs/features")]
    [InlineData("""{ "approval": "merge" }""", false, "docs/features")]
    [InlineData("""{ "docs": { "enabled": true }, "delegation": { "roles": { "research": { "model": "haiku" } } } }""", true, "docs/features")]
    public async Task TheDocsSectionOfTheJobFileTurnsDocumentationOnWithItsFolderAsync(string text, bool enabled, string folder)
    {
        var files = new CommittedFiles().With(Worktree, DocsRulesReader.JobFile, text);

        Assert.Equal(new DocsRules(enabled, folder), Outcomes.Succeeds(await Reader(files, new Documented.Folders()).OfWorktreeAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task WithoutAJobFileDocumentationIsOffAsync() =>
        Assert.Equal(DocsRules.Off, Outcomes.Succeeds(await Reader(new CommittedFiles().Workspace(Worktree), new Documented.Folders()).OfWorktreeAsync(Worktree, Cancellation)));

    [Theory]
    [InlineData("not json", "Malformed")]
    [InlineData("""{ "docs": true }""", "Malformed")]
    [InlineData("""{ "docs": { "enabled": "yes" } }""", "Malformed")]
    [InlineData("""{ "docs": { "enabled": true, "folder": 3 } }""", "Malformed")]
    [InlineData("""{ "docs": { "enabled": true, "c4": true } }""", "UnknownField")]
    [InlineData("""{ "docs": { "enabled": true, "folder": "" } }""", "InvalidFolder")]
    [InlineData("""{ "docs": { "enabled": true, "folder": "/docs" } }""", "InvalidFolder")]
    [InlineData("""{ "docs": { "enabled": true, "folder": "docs/../../etc" } }""", "InvalidFolder")]
    [InlineData("""{ "docs": { "enabled": true, "folder": "docs\\features" } }""", "InvalidFolder")]
    [InlineData("""{ "docs": { "enabled": true, "folder": { "path": "docs" } } }""", "Malformed")]
    public async Task AnInvalidDocsSectionIsRejectedWithItsErrorAsync(string text, string error)
    {
        var files = new CommittedFiles().With(Worktree, DocsRulesReader.JobFile, text);

        Assert.Equal(Enum.Parse<DocumentationError>(error), Outcomes.FailsWith(await Reader(files, new Documented.Folders()).OfWorktreeAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task AJobFileOverItsSizeOrAWorktreeThatCannotBeReadIsRejectedAsync()
    {
        var large = new CommittedFiles().With(Worktree, DocsRulesReader.JobFile, $$"""{ "approval": "{{new string('a', DocsRulesReader.MaximumBytes)}}" }""");

        Assert.Equal(DocumentationError.TooLarge, Outcomes.FailsWith(await Reader(large, new Documented.Folders()).OfWorktreeAsync(Worktree, Cancellation)));
        Assert.Equal(DocumentationError.Unreadable, Outcomes.FailsWith(await Reader(new CommittedFiles(), new Documented.Folders()).OfWorktreeAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task SwitchingDocsOnWritesTheSectionAndKeepsTheRestOfTheJobFileAsync()
    {
        var empty = new Documented.Folders();
        var declared = new Documented.Folders();
        declared.Draft(Repository, DocsRulesReader.JobFile, """{ "approval": "merge", "docs": { "enabled": false, "folder": "notes" } }""");

        var fresh = Outcomes.Succeeds(await Reader(new CommittedFiles(), empty).SwitchAsync(Repository, enabled: true, Cancellation));
        var kept = Outcomes.Succeeds(await Reader(new CommittedFiles(), declared).SwitchAsync(Repository, enabled: true, Cancellation));

        Assert.Equal((new DocsRules(true, "docs/features"), new DocsRules(true, "notes")), (fresh, kept));
        Assert.Equal("{\n  \"docs\": {\n    \"enabled\": true\n  }\n}\n", empty[Repository, DocsRulesReader.JobFile]);
        Assert.Equal("{\n  \"approval\": \"merge\",\n  \"docs\": {\n    \"enabled\": true,\n    \"folder\": \"notes\"\n  }\n}\n", declared[Repository, DocsRulesReader.JobFile]);
    }

    [Fact]
    public async Task AJobFileThatDoesNotParseIsNotSwitchedAsync()
    {
        var broken = new Documented.Folders();
        broken.Draft(Repository, DocsRulesReader.JobFile, "{ not json");

        Assert.Equal(DocumentationError.Malformed, Outcomes.FailsWith(await Reader(new CommittedFiles(), broken).SwitchAsync(Repository, enabled: true, Cancellation)));
        Assert.Equal("{ not json", broken[Repository, DocsRulesReader.JobFile]);
    }

    [Fact]
    public async Task TheRulesAreReadAsTheWorkingCopyHasThemAndAsTheyAreCommittedAsync()
    {
        var committed = new CommittedFiles().With(Repository, DocsRulesReader.JobFile, """{ "approval": "keep" }""");
        var working = new Documented.Folders();
        working.Draft(Repository, DocsRulesReader.JobFile, """{ "approval": "keep", "docs": { "enabled": true } }""");

        var rules = await Reader(committed, working).StateAsync(Repository, Cancellation);

        Assert.Equal(new DocsRulesState(new DocsRules(true, "docs/features"), DocsRules.Off), rules);
    }

    [Theory]
    [InlineData(true, "Turn on feature documentation")]
    [InlineData(false, "Turn off feature documentation")]
    public async Task CommittingTheRulesCommitsTheJobFileAloneSayingWhatItTurnsAsync(bool enabled, string message)
    {
        var working = new Documented.Folders();
        working.Draft(Repository, DocsRulesReader.JobFile, $$"""{ "docs": { "enabled": {{(enabled ? "true" : "false")}} } }""");

        var committed = Outcomes.Succeeds(await Reader(new CommittedFiles(), working).CommitAsync(Repository, Cancellation));

        Assert.Equal(new DocsCommit("c0ffee0c0ffee0c0ffee0c0ffee0c0ffee0c0ff", "main"), committed);
        Assert.Equal([(Repository, DocsRulesReader.JobFile, message)], working.Committed);
    }

    [Theory]
    [InlineData(WorkspaceFailure.NoBaseBranch, DocumentationError.NotOnABranch)]
    [InlineData(WorkspaceFailure.NoGitIdentity, DocumentationError.NoGitIdentity)]
    [InlineData(WorkspaceFailure.FileUnchanged, DocumentationError.NothingToCommit)]
    [InlineData(WorkspaceFailure.GitFailed, DocumentationError.NotCommitted)]
    public async Task ACommitTheCheckoutRefusesIsReportedAsItsDocumentationErrorAsync(WorkspaceFailure refusal, DocumentationError error)
    {
        var working = new Documented.Folders { Refusal = refusal };
        working.Draft(Repository, DocsRulesReader.JobFile, """{ "docs": { "enabled": true } }""");

        Assert.Equal(error, Outcomes.FailsWith(await Reader(new CommittedFiles(), working).CommitAsync(Repository, Cancellation)));
    }

    private static DocsRulesReader Reader(CommittedFiles committed, Documented.Folders working) => new(committed, working, working);
}
