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
    }

    [Fact]
    public async Task SwitchingTurnsDocsOnInTheJobFileAndSaysItAppliesOnceCommittedAsync()
    {
        var setting = new DocsSettingViewModel(new FakeDocumentation(), new FakeRoles());
        await setting.ReadAsync(Repository, Cancellation);
        var before = (setting.IsOn, setting.Writer);

        await setting.SwitchCommand.ExecuteAsync(null);

        Assert.Equal((false, "research · the harness's default model"), before);
        Assert.Equal((true, DocsPhrases.Saved, string.Empty), (setting.IsOn, setting.Note, setting.Error));
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
