using Avala.Testing;
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
        var setting = new DocsSettingViewModel(documentation, new FakeRoles { Research = new ModelChoice(Option<string>.Some("haiku"), Option<string>.Some("low")) }, SiteJobs.Focus());

        await setting.ReadAsync(Repository, Cancellation);

        Assert.Equal((true, true, "notes/features", "research · haiku · low", DocsPhrases.Committed), (setting.IsShown, setting.IsOn, setting.Folder, setting.Writer, setting.Note));
        Assert.False(setting.IsUncommitted);
        Assert.False(setting.CommitCommand.CanExecute(null));
    }

    [Fact]
    public async Task SwitchingTurnsDocsOnInTheWorkingCopyAndSaysNewJobsRunWithoutThemUntilCommittedAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation(), new FakeRoles(), SiteJobs.Focus());
        await setting.ReadAsync(Repository, Cancellation);
        var before = (setting.IsOn, setting.Writer);

        await setting.SwitchCommand.ExecuteAsync(null);

        Assert.Equal((false, "research · the harness's default model"), before);
        Assert.Equal((true, true, DocsPhrases.Uncommitted(on: true), string.Empty), (setting.IsOn, setting.IsUncommitted, setting.Note, setting.Error));
    }

    [Fact]
    public async Task DocsOnInTheWorkingCopyOnlyAreShownOnSayingSoAndOfferToCommitTheJobFileAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation { Working = new DocsRules(true, "docs/features") }, new FakeRoles(), SiteJobs.Focus());

        await setting.ReadAsync(Repository, Cancellation);

        Assert.Equal((true, true, DocsPhrases.Uncommitted(on: true)), (setting.IsOn, setting.IsUncommitted, setting.Note));
        Assert.True(setting.CommitCommand.CanExecute(null));
    }

    [Fact]
    public async Task CommittingTheJobFileSaysWhereItLandedAndThatNewJobsFollowItAsync()
    {
        var documentation = new FakeDocumentation { Working = new DocsRules(true, "docs/features") };
        var setting = new DocsSettingViewModel(documentation, new FakeRoles(), SiteJobs.Focus());
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
        var setting = new DocsSettingViewModel(new FakeDocumentation { Working = new DocsRules(true, "docs/features"), CommitRefusal = refusal }, new FakeRoles(), SiteJobs.Focus());
        await setting.ReadAsync(Repository, Cancellation);

        await setting.CommitCommand.ExecuteAsync(null);

        Assert.Equal((true, error), (setting.IsUncommitted, setting.Error));
    }

    [Fact]
    public async Task AJobFileThatCannotBeSwitchedLeavesDocsAsTheyWereAndSaysWhyAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation { Switched = DocumentationError.Malformed }, new FakeRoles(), SiteJobs.Focus());
        await setting.ReadAsync(Repository, Cancellation);

        await setting.SwitchCommand.ExecuteAsync(null);

        Assert.Equal((false, DocsPhrases.NotSwitched(DocumentationError.Malformed)), (setting.IsOn, setting.Error));
    }

    [Fact]
    public async Task CreatingTheDocsSiteSaysItsJobPreparesItOffersItNoMoreAndOpensThatJobAsync()
    {
        var documentation = new FakeDocumentation { Docs = new RepositoryDocs(true, "docs/features", []) };
        var messenger = new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger();
        var selected = new List<Jobs.Contracts.JobId>();
        CommunityToolkit.Mvvm.Messaging.IMessengerExtensions.Register<object, Contracts.Presentation.JobSelected>(messenger, selected, (_, message) => selected.Add(message.Job));
        var setting = new DocsSettingViewModel(documentation, new FakeRoles(), SiteJobs.Focus(messenger));
        await setting.ReadAsync(Repository, Cancellation);
        var before = setting.CanCreateSite;

        await setting.CreateSiteCommand.ExecuteAsync(null);
        setting.OpenSiteJobCommand.Execute(null);

        Assert.True(before);
        Assert.Equal([Repository], documentation.Created);
        Assert.Equal((false, true, "Being prepared by its job. Review it like any job: the site is here once it is merged."), (setting.CanCreateSite, setting.IsSitePreparing, setting.Site));
        Assert.Equal([Outcomes.Present(documentation.Site.Preparing)], selected);
    }

    [Fact]
    public async Task AnExistingSiteThatDocfxCannotServeSaysHowToInstallItAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation { Site = new DocsSite(true, "docs", false) }, new FakeRoles(), SiteJobs.Focus());

        await setting.ReadAsync(Repository, Cancellation);

        Assert.Equal(("In docs. To open it, install DocFX: dotnet tool install -g docfx", false), (setting.Site, setting.CanCreateSite));
    }
}
