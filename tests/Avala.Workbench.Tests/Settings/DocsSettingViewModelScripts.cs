using Avala.Agents.Contracts.Sessions;
using Avala.Documentation.Contracts;
using Avala.Sdk;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class DocsSettingViewModelScripts
{
    private const string Repository = "/repos/ledger";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositoryShowsWhetherDocsAreOnWithTheirFolderAndTheResearchRoleThatWritesThemAsync()
    {
        var documentation = new FakeDocumentation { Docs = new RepositoryDocs(true, "notes/features", []) };
        var setting = new DocsSettingViewModel(documentation, new FakeRoles { Research = new ModelChoice(Option<string>.Some("haiku"), Option<string>.Some("low")) });

        await setting.ReadAsync(Repository, Cancellation);

        Assert.Equal((true, true, "notes/features", "research · haiku · low", DocsPhrases.Committed), (setting.IsShown, setting.IsOn, setting.Folder, setting.Writer, setting.Note));
        Assert.False(setting.IsUncommitted);
        Assert.False(setting.CommitCommand.CanExecute(null));
    }

    [Fact]
    public async Task SwitchingTurnsDocsOnInTheWorkingCopyAndSaysNewJobsRunWithoutThemUntilCommittedAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation(), new FakeRoles());
        await setting.ReadAsync(Repository, Cancellation);
        var before = (setting.IsOn, setting.Writer);

        await setting.SwitchCommand.ExecuteAsync(null);

        Assert.Equal((false, "research · the harness's default model"), before);
        Assert.Equal((true, true, DocsPhrases.Uncommitted(on: true), string.Empty), (setting.IsOn, setting.IsUncommitted, setting.Note, setting.Error));
    }

    [Fact]
    public async Task DocsOnInTheWorkingCopyOnlyAreShownOnSayingSoAndOfferToCommitTheJobFileAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation { Working = new DocsRules(true, "docs/features") }, new FakeRoles());

        await setting.ReadAsync(Repository, Cancellation);

        Assert.Equal((true, true, DocsPhrases.Uncommitted(on: true)), (setting.IsOn, setting.IsUncommitted, setting.Note));
        Assert.True(setting.CommitCommand.CanExecute(null));
    }

    [Fact]
    public async Task CommittingTheJobFileSaysWhereItLandedAndThatNewJobsFollowItAsync()
    {
        var documentation = new FakeDocumentation { Working = new DocsRules(true, "docs/features") };
        var setting = new DocsSettingViewModel(documentation, new FakeRoles());
        await setting.ReadAsync(Repository, Cancellation);

        await setting.CommitCommand.ExecuteAsync(null);

        Assert.Equal([Repository], documentation.Committed);
        Assert.Equal((true, false, "Committed .avala/jobs.json as 3f9a1c2 on main. New jobs follow it.", string.Empty), (setting.IsOn, setting.IsUncommitted, setting.Note, setting.Error));
        Assert.False(setting.CommitCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(DocumentationError.NotOnABranch, "Not committed: the repository is not on a branch. Check out a branch, then commit again.")]
    [InlineData(DocumentationError.NoGitIdentity, "Not committed: git has no name and email for you. Set user.name and user.email, then commit again.")]
    [InlineData(DocumentationError.NothingToCommit, "Nothing to commit: .avala/jobs.json is already as committed.")]
    [InlineData(DocumentationError.NotCommitted, "Not committed: git refused the commit. Commit .avala/jobs.json yourself.")]
    public async Task ACommitGitRefusesKeepsTheJobFileUncommittedAndSaysWhyAsync(DocumentationError refusal, string error)
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation { Working = new DocsRules(true, "docs/features"), CommitRefusal = refusal }, new FakeRoles());
        await setting.ReadAsync(Repository, Cancellation);

        await setting.CommitCommand.ExecuteAsync(null);

        Assert.Equal((true, error), (setting.IsUncommitted, setting.Error));
    }

    [Fact]
    public async Task AJobFileThatCannotBeSwitchedLeavesDocsAsTheyWereAndSaysWhyAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation { Switched = DocumentationError.Malformed }, new FakeRoles());
        await setting.ReadAsync(Repository, Cancellation);

        await setting.SwitchCommand.ExecuteAsync(null);

        Assert.Equal((false, DocsPhrases.NotSwitched(DocumentationError.Malformed)), (setting.IsOn, setting.Error));
    }

    [Fact]
    public async Task CreatingTheDocsSiteSaysWhatWasWrittenAndThatAJobFillsItAndThenOffersItNoMoreAsync()
    {
        var documentation = new FakeDocumentation { Docs = new RepositoryDocs(true, "docs/features", []) };
        var setting = new DocsSettingViewModel(documentation, new FakeRoles());
        await setting.ReadAsync(Repository, Cancellation);
        var before = setting.CanCreateSite;

        await setting.CreateSiteCommand.ExecuteAsync(null);

        Assert.True(before);
        Assert.Equal([Repository], documentation.Created);
        Assert.False(setting.CanCreateSite);
        Assert.Equal("In docs. Open it from a job's Docs. Wrote 1 file.", setting.Site);
    }

    [Fact]
    public async Task AnExistingSiteThatDocfxCannotServeSaysHowToInstallItAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation { Site = new DocsSite(true, "docs", false) }, new FakeRoles());

        await setting.ReadAsync(Repository, Cancellation);

        Assert.Equal(("In docs. To open it, install DocFX: dotnet tool install -g docfx", false), (setting.Site, setting.CanCreateSite));
    }
}
