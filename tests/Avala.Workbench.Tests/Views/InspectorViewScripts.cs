using Avala.Components.UI;
using Avala.Testing.UI;
using Avala.Workbench.Inspector;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

internal static class Sections
{
    public static ViewScript Open(ViewScript view)
    {
        view.Find<Fold>("Section").IsExpanded = true;

        return view.Settle();
    }

    public static ViewScript ClickHeader(ViewScript view) =>
        view.Click(view.Find<Fold>("Section").GetVisualDescendants().OfType<ToggleButton>().First());

    public static (object? Header, string? Fact, bool Open) Row(ViewScript view) =>
        (view.Find<Fold>("Section").Header, view.Find<Fold>("Section").Fact, view.Find<Fold>("Section").IsExpanded);
}

public sealed class EvidenceSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheEvidenceSectionIsFoldedWithItsFactAndOpensOnAClickToTheVerdictAndEachAttemptAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignEvidenceSectionViewModel());
            var folded = (Sections.Row(view), view.Shows("Summary"));

            Sections.ClickHeader(view);

            Assert.Equal((((object?)"Evidence", (string?)"4 of 4 checks passed", false), false), folded);
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
            var view = Sections.Open(Screen.Show(section));

            Assert.True(view.Shows("Loading"));
        }, TestContext.Current.CancellationToken);
}

public sealed class AuditSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheDecisionsSectionIsFoldedUntilOpenedAndThenListsDecisionsAndAssumptionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAuditSectionViewModel());
            var folded = (Sections.Row(view), view.Shows("Summary"));

            Sections.ClickHeader(view);

            Assert.Equal((((object?)"Decisions", (string?)"1 assumption", false), false), folded);
            Assert.Equal((3, 1), (view.Find<ItemsControl>("Decisions").ItemCount, view.Find<ItemsControl>("Assumptions").ItemCount));
            Assert.Contains("How many failed logins before the limit?: 5 attempts per minute", view.VisibleTexts);
            Assert.False(view.HasClass("Section", "attention"));
        }, TestContext.Current.CancellationToken);
}

public sealed class UsageSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheUsageSectionIsOpenWithSpendCapsAndTheCostMeterAndHidesAnEmptyCarveAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageSectionViewModel());

            Assert.Equal(((object?)"Usage & caps", (string?)"USD 0.84 of USD 5", true), Sections.Row(view));
            Assert.Equal("USD 0.84 · 61,250 tokens", view.TextOf("Spent"));
            Assert.Equal(2, view.Find<ItemsControl>("Caps").ItemCount);
            Assert.Equal((false, true), (view.Shows("Carve"), view.Shows("Meter")));
            Assert.Contains("17%", view.VisibleTexts);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingTheOpenUsageHeaderFoldsItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageSectionViewModel());

            Sections.ClickHeader(view);

            Assert.Equal((false, false), (Sections.Row(view).Open, view.Shows("Spent")));
        }, TestContext.Current.CancellationToken);
}

public sealed class AutonomySectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheAutonomySectionShowsTheAutonomyAndTheConnectionInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Sections.Open(Screen.Show(new DesignAutonomySectionViewModel()));

            Assert.Equal((string?)"Supervised", Sections.Row(view).Fact);
            Assert.Equal(("Supervised, as the repository declares", "claude-personal"), (view.TextOf("Autonomy"), view.TextOf("Connection")));
            Assert.Equal("JetBrains Mono", view.Find<TextBlock>("Connection").FontFamily.FamilyNames[0]);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AConnectionChosenByCapacityShowsWhyAndTheReadingsItComparedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Sections.Open(Screen.Show(new DesignAutonomySectionViewModel()));

            Assert.Equal((true, "Chosen by capacity: claude-personal had the most left"), (view.Shows("Reason"), view.TextOf("Reason")));
            Assert.Equal(2, view.Find<ItemsControl>("Compared").ItemCount);
            Assert.Contains("88% of 5h · holds at 90%", view.VisibleTexts, StringComparer.Ordinal);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AConnectionNamedOnTheJobShowsNoComparisonAsync() =>
        ui.RunAsync(() =>
        {
            var view = Sections.Open(Screen.Show(new Named()));

            Assert.Equal((false, false), (view.Shows("Reason"), view.Shows("Compared")));
        }, TestContext.Current.CancellationToken);

    private sealed class Named : IAutonomySectionViewModel
    {
        public bool IsLoaded => true;

        public string Autonomy => "Supervised, as asked";

        public string Fact => "Supervised";

        public string Connection => "claude-work";

        public string Reason => string.Empty;

        public IReadOnlyList<CapacityLine> Compared { get; } = [];
    }
}

public sealed class WorktreeSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheWorktreeSectionShowsBranchBasePathAndPortsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Sections.Open(Screen.Show(new DesignWorktreeSectionViewModel()));

            Assert.Equal((string?)"rate-limit-post-login", Sections.Row(view).Fact);
            Assert.Equal(("avala/rate-limit-post-login", "main at 4f2c9e1", "Ports 41000–41009"), (view.TextOf("Branch"), view.TextOf("Base"), view.TextOf("Ports")));
            Assert.False(view.Shows("NoWorktree"));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheDelegationSectionIsOpenAndShowsTheParentAndNoEmptyNoteAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationSectionViewModel());

            Assert.Equal(((object?)"Delegation", (string?)"a sub-agent", true), Sections.Row(view));
            Assert.Equal(("Delegated by Harden the auth endpoints", false), (view.TextOf("DelegatedBy"), view.Shows("NoDelegation")));
        }, TestContext.Current.CancellationToken);
}
