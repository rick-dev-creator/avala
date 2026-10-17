using Avala.Documentation.Contracts;
using Avala.Documentation.RepositoryFiles;
using Avala.Testing;

namespace Avala.Documentation.Tests.RepositoryFiles;

public sealed class DocsRulesTests
{
    private const string Worktree = "/worktrees/job-1";

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

        Assert.Equal(new DocsRules(enabled, folder), Outcomes.Succeeds(await new DocsRulesReader(files).OfWorktreeAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task WithoutAJobFileDocumentationIsOffAsync() =>
        Assert.Equal(DocsRules.Off, Outcomes.Succeeds(await new DocsRulesReader(new CommittedFiles().Workspace(Worktree)).OfWorktreeAsync(Worktree, Cancellation)));

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

        Assert.Equal(Enum.Parse<DocumentationError>(error), Outcomes.FailsWith(await new DocsRulesReader(files).OfWorktreeAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task AJobFileOverItsSizeOrAWorktreeThatCannotBeReadIsRejectedAsync()
    {
        var large = new CommittedFiles().With(Worktree, DocsRulesReader.JobFile, $$"""{ "approval": "{{new string('a', DocsRulesReader.MaximumBytes)}}" }""");

        Assert.Equal(DocumentationError.TooLarge, Outcomes.FailsWith(await new DocsRulesReader(large).OfWorktreeAsync(Worktree, Cancellation)));
        Assert.Equal(DocumentationError.Unreadable, Outcomes.FailsWith(await new DocsRulesReader(new CommittedFiles()).OfWorktreeAsync(Worktree, Cancellation)));
    }
}
