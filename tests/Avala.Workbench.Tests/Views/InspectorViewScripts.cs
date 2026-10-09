using Avala.Testing.UI;
using Avala.Workbench.Inspector;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class EvidenceSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheEvidenceSectionIsOpenAndShowsTheVerdictAndEachAttemptAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignEvidenceSectionViewModel());

            Assert.True(view.Find<Expander>("Section").IsExpanded);
            Assert.Equal("Verified on attempt 2 of 2", view.TextOf("Summary"));
            Assert.Equal(2, view.Find<ItemsControl>("Attempts").ItemCount);
            Assert.False(view.Shows("Loading"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ASectionStillLoadingSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            using var section = new EvidenceSectionViewModel(bench.Inspected());
            var view = Screen.Show(section);

            Assert.True(view.Shows("Loading"));
        }, TestContext.Current.CancellationToken);
}

public sealed class AuditSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheAuditSectionIsCollapsedUntilOpenedAndThenListsDecisionsAndAssumptionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAuditSectionViewModel());
            var collapsed = view.Shows("Summary");

            view.Find<Expander>("Section").IsExpanded = true;
            view.Settle();

            Assert.False(collapsed);
            Assert.Equal((3, 1), (view.Find<ItemsControl>("Decisions").ItemCount, view.Find<ItemsControl>("Assumptions").ItemCount));
            Assert.Contains("How many failed logins before the limit?: 5 attempts per minute", view.VisibleTexts);
        }, TestContext.Current.CancellationToken);
}

public sealed class UsageSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheUsageSectionShowsSpendAndCapsAndHidesAnEmptyCarveAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageSectionViewModel());
            view.Find<Expander>("Section").IsExpanded = true;
            view.Settle();

            Assert.Equal("USD 0.84 · 61,250 tokens", view.TextOf("Spent"));
            Assert.Equal(2, view.Find<ItemsControl>("Caps").ItemCount);
            Assert.False(view.Shows("Carve"));
        }, TestContext.Current.CancellationToken);
}

public sealed class AutonomySectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheAutonomySectionShowsTheAutonomyAndTheConnectionInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAutonomySectionViewModel());
            view.Find<Expander>("Section").IsExpanded = true;
            view.Settle();

            Assert.Equal(("Supervised, as the repository declares", "claude-personal"), (view.TextOf("Autonomy"), view.TextOf("Connection")));
            Assert.Equal("JetBrains Mono", view.Find<TextBlock>("Connection").FontFamily.FamilyNames[0]);
        }, TestContext.Current.CancellationToken);
}

public sealed class WorktreeSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheWorktreeSectionShowsBranchBasePathAndPortsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreeSectionViewModel());
            view.Find<Expander>("Section").IsExpanded = true;
            view.Settle();

            Assert.Equal(("avala/rate-limit-post-login", "main at 4f2c9e1", "Ports 41000–41009"), (view.TextOf("Branch"), view.TextOf("Base"), view.TextOf("Ports")));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheDelegationSectionShowsTheParentAndNoEmptyNoteAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationSectionViewModel());
            view.Find<Expander>("Section").IsExpanded = true;
            view.Settle();

            Assert.Equal(("Delegated by Harden the auth endpoints", false), (view.TextOf("DelegatedBy"), view.Shows("NoDelegation")));
        }, TestContext.Current.CancellationToken);
}
