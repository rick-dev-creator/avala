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
    public Task AHandedOffJobShowsEachHandoffAndTheSpendOnEachConnectionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Sections.Open(Screen.Show(new DesignAutonomySectionViewModel()));

            Assert.Equal(2, view.Find<ItemsControl>("Handoffs").ItemCount);
            Assert.Contains("Handed off from claude-work to claude-personal at 91% of the 5-hour window · 2026-10-10 10:42", view.VisibleTexts, StringComparer.Ordinal);
            Assert.Contains("Spent on claude-work: 1.84 USD · 412,880 tokens", view.VisibleTexts, StringComparer.Ordinal);
            Assert.Contains("Spent on claude-personal: 0.31 USD · 61,200 tokens", view.VisibleTexts, StringComparer.Ordinal);
            Assert.False(view.Shows("Waiting"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobWaitingForAResetSaysWhenItResumesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Sections.Open(Screen.Show(new Waiting()));

            Assert.Equal((true, "Resumes at 03:10 when the 5-hour window resets"), (view.Shows("Waiting"), view.TextOf("Waiting")));
            Assert.False(view.Shows("Handoffs"));
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

        public IReadOnlyList<HandoffLine> Handoffs { get; } = [];

        public string Waiting => string.Empty;
    }

    private sealed class Waiting : IAutonomySectionViewModel
    {
        public bool IsLoaded => true;

        public string Autonomy => "Supervised, as asked";

        public string Fact => "Supervised";

        public string Connection => "claude-work";

        public string Reason => string.Empty;

        public IReadOnlyList<CapacityLine> Compared { get; } = [];

        public IReadOnlyList<HandoffLine> Handoffs { get; } = [];

        string IAutonomySectionViewModel.Waiting => "Resumes at 03:10 when the 5-hour window resets";
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

    [Fact]
    public Task ALongWorktreeIdIsEllipsizedAndNeverTruncatesTheSectionsHeaderAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new LongWorktree(), Avalonia.Styling.ThemeVariant.Dark, 300, 400);
            var texts = view.Find<Fold>("Section").GetVisualDescendants().OfType<TextBlock>().ToList();
            var header = texts.Single(text => text.Text == "Worktree");
            var fact = texts.Single(text => text.Name == "PART_Fact");

            Assert.True(header.Bounds.Width >= Natural(header) - 0.5, $"the header is {header.Bounds.Width} wide of {Natural(header)}");
            Assert.True(fact.Bounds.Width is > 40 and var shown && shown < Natural(fact), $"the id is {fact.Bounds.Width} wide of {Natural(fact)}");
        }, TestContext.Current.CancellationToken);

    private static double Natural(TextBlock text)
    {
        var probe = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily, FontWeight = text.FontWeight, Margin = text.Margin };
        probe.Measure(Avalonia.Size.Infinity);

        return probe.DesiredSize.Width;
    }

    private sealed class LongWorktree : IWorktreeSectionViewModel
    {
        public bool IsLoaded => true;

        public string Branch => "avala/01a122dd1eef75e58739993947fac499";

        public string Fact => "01a122dd1eef75e58739993947fac499";

        public string Base => "main at 4f2c9e1";

        public string Path => "~/.avala/worktrees/dogfood/01a122dd1eef75e58739993947fac499";

        public string Ports => string.Empty;
    }
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
