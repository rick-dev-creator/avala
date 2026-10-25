using Avala.Testing.UI;
using Avala.Workbench.Settings;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class DocsSettingViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task WithDocsOnTheSwitchIsOnAndTheFolderAndWriterAreShownAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDocsSettingViewModel(), ThemeVariant.Dark, 800, 500);

            Assert.Equal((true, "docs/features", "research · haiku · low"), (view.Find<ToggleSwitch>("DocsSwitch").IsChecked, view.TextOf("DocsFolder"), view.TextOf("DocsWriter")));
        }, Cancellation);

    [Fact]
    public Task WithDocsOffOnlyTheSwitchAndItsNoteShowAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDocsSettingViewModel { IsOn = false }, ThemeVariant.Dark, 800, 500);

            Assert.Equal((false, false, true), (view.Find<ToggleSwitch>("DocsSwitch").IsChecked, view.Shows("DocsDetails"), view.Shows("DocsNote")));
        }, Cancellation);

    [Fact]
    public Task RulesLeftUncommittedShowTheirNoteWithAnAttentionDotAndTheCommitButtonOnlyThenAsync() =>
        ui.RunAsync(() =>
        {
            var uncommitted = Screen.Show(new DesignDocsSettingViewModel(), ThemeVariant.Dark, 800, 500);
            var shown = (uncommitted.Shows("CommitRules"), uncommitted.All<Avalonia.Controls.Shapes.Ellipse>().Single(dot => dot.Classes.Contains("dot")).Classes.Contains("attention"), uncommitted.TextOf("DocsNote"));
            var committed = Screen.Show(new DesignDocsSettingViewModel { IsUncommitted = false }, ThemeVariant.Dark, 800, 500);

            Assert.Equal((true, true, DocsPhrases.Uncommitted(on: true)), shown);
            Assert.False(committed.Shows("CommitRules"));
        }, Cancellation);

    [Fact]
    public Task TheRepositorysDocsTabShowsFeatureDocumentationAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRepositorySettingsViewModel { Tab = RepositoryTab.Docs }, ThemeVariant.Dark, 1200, 1600);

            Assert.True(view.Shows("DocsSwitch"));
        }, Cancellation);
}
