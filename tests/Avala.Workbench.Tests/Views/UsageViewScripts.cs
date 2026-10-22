using Avala.Components.UI.Graphs;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Usage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class UsageViewScripts(HeadlessUi ui)
{
    private static readonly string[] Headers = ["Title", "Scope", "JobsTitle", "JobsHint", "RangeTitle", "Caption", "Label", "Tokens"];

    private static readonly string[] CardParts = ["ConnectionName", "Provider", "Cost", "Tokens"];

    private static bool ReadWhole(TextBlock text, Visual page)
    {
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight);
        var longest = (text.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => new Avalonia.Media.TextFormatting.TextLayout(word, typeface, text.FontSize, null).Width).DefaultIfEmpty(0).Max();
        var end = text.TranslatePoint(new Point(text.Bounds.Width, 0), page)!.Value.X;

        return text.Bounds.Width + 0.5 >= longest && end <= page.Bounds.Width && text.TextLayout.TextLines.All(line => !line.HasCollapsed && line.Width <= text.Bounds.Width + 0.5);
    }

    private static bool Save(ViewScript view, string name)
    {
        if (Environment.GetEnvironmentVariable("AVALA_FEATURE_SCREENS") is not { Length: > 0 } folder || Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(view.Window) is not { } frame)
        {
            return false;
        }

        frame.Save(Path.Combine(Directory.CreateDirectory(folder).FullName, $"{name}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return true;
    }

    [Fact]
    public Task ThePageShowsConnectionsJobsTokenWindowsAndInterventionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageViewModel());

            Assert.Equal((2, 7, 6, 2), (view.Find<ItemsControl>("Connections").ItemCount, view.Find<ItemsControl>("Days").ItemCount, view.Find<ItemsControl>("Jobs").ItemCount, view.Find<ItemsControl>("InterventionList").ItemCount));
            Assert.Equal((false, false, true, "All recorded usage, kept across restarts"), (view.Shows("NoConnections"), view.Shows("NoJobs"), view.Shows("InterventionsGroup"), view.TextOf("Scope")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AtItsNarrowestThePagesHeadersAreReadWholeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageViewModel(), Avalonia.Styling.ThemeVariant.Dark, 272, 1600);
            view.Settle();

            var clipped = Headers
                .Select(name => view.Window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == name && text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text)))
                .Where(text => !ReadWhole(text, view.Window))
                .Select(text => $"{text.Name} '{text.Text}' is given {text.Bounds.Width:0}");

            Assert.Empty(clipped);
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task ThePageAtItsNarrowestAsync(string variant)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AVALA_FEATURE_SCREENS") is { Length: > 0 }, "Set AVALA_FEATURE_SCREENS to a folder and AVALA_HEADLESS_RENDER=1 to capture the page.");

        return ui.RunAsync(() =>
        {
            var view = Screen.ShowStill(new DesignUsageViewModel(), variant == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light, 272, 2300);
            view.Settle();

            Assert.True(Save(view, $"usage-272-{variant}"));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task AtItsNarrowestAConnectionsNameProviderCostAndTokensStayApartAndInsideItsCardAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageViewModel(), Avalonia.Styling.ThemeVariant.Dark, 272, 1600);
            view.Settle();
            var card = view.Window.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Meter");
            var parts = CardParts
                .Select(name => card.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == name))
                .Select(text => (text.Name, Box: new Rect(text.TranslatePoint(default, card)!.Value, new Size(Math.Max(text.Bounds.Width, text.TextLayout.WidthIncludingTrailingWhitespace), text.Bounds.Height))))
                .ToList();

            var faults = parts.SelectMany(part => parts.Where(other => string.CompareOrdinal(part.Name, other.Name) < 0 && part.Box.Intersects(other.Box)).Select(other => $"{part.Name} {part.Box} overlaps {other.Name} {other.Box}"))
                .Concat(parts.Where(part => part.Box.Right > card.Bounds.Width - card.Padding.Right + 0.5).Select(part => $"{part.Name} ends at {part.Box.Right:0} in a card {card.Bounds.Width:0} wide"));

            Assert.Empty(faults);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AtItsNarrowestEveryJobsTitleShowsAtLeastItsFirstWordAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageViewModel(), Avalonia.Styling.ThemeVariant.Dark, 272, 1600);
            view.Settle();
            var jobs = view.Window.GetVisualDescendants().OfType<ItemsControl>().First(list => list.Name == "Jobs");

            var cut = jobs.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Name == "Title" && text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
                .Where(text => text.Bounds.Width + 0.5 < new Avalonia.Media.TextFormatting.TextLayout(text.Text!.Split(' ')[0], new Typeface(text.FontFamily, text.FontStyle, text.FontWeight), text.FontSize, null).Width)
                .Select(text => $"'{text.Text}' is given {text.Bounds.Width:0}");

            Assert.Empty(cut);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AtItsNarrowestEveryInterventionsReasonReadsWholeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageViewModel(), Avalonia.Styling.ThemeVariant.Dark, 272, 1600);
            view.Settle();
            var interventions = view.Window.GetVisualDescendants().OfType<ItemsControl>().First(list => list.Name == "InterventionList");

            var clipped = interventions.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Name == "Reason" && text.IsEffectivelyVisible && !ReadWhole(text, view.Window))
                .Select(text => $"'{text.Text}' is given {text.Bounds.Width:0}");

            Assert.Empty(clipped);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task BeforeAnyUsageThePageSaysSoAndHidesInterventionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new EmptyUsage());

            Assert.Equal((true, true, false), (view.Shows("NoConnections"), view.Shows("NoJobs"), view.Shows("InterventionsGroup")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ADaysCostAtApiPricesReadsWholeOnItsRowAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PricedDay(), Avalonia.Styling.ThemeVariant.Dark, 1200, 200);
            var cost = view.Find<TextBlock>("Cost");
            cost.Measure(Size.Infinity);

            Assert.True(cost.DesiredSize.Width <= cost.Bounds.Width, $"the cost needs {cost.DesiredSize.Width} and is given {cost.Bounds.Width}");
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task BesideTheDefaultSidebarAtTheNarrowestWindowABusyDaysCostReadsWholeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new PricedDay("48,512,330 tokens", "142.87 USD at API prices"), Avalonia.Styling.ThemeVariant.Dark, 412, 200);
            var cost = view.Find<TextBlock>("Cost");
            var end = cost.TranslatePoint(new Point(cost.Bounds.Width, 0), view.Window)!.Value.X;
            cost.Measure(Size.Infinity);

            Assert.True(cost.DesiredSize.Width <= cost.Bounds.Width && end <= view.Window.Bounds.Width, $"the cost needs {cost.DesiredSize.Width:0}, is given {cost.Bounds.Width:0} and ends at {end:0} on a row {view.Window.Bounds.Width:0} wide");
        }, TestContext.Current.CancellationToken);

    private sealed class PricedDay(string tokens = "15,275 tokens", string cost = "0.06 USD at API prices") : IUsageDayViewModel
    {
        public string Day => "Sun 18 Oct · today";

        public string Tokens => tokens;

        public string Cost => cost;

        public string Types => "15,275 input";

        public double Share => 1;

        public bool IsToday => true;
    }

    private sealed class EmptyUsage : IUsageViewModel
    {
        public Avala.Workbench.Presenting.IReadProblemViewModel ReadProblem { get; } = new Avala.Workbench.Presenting.DesignReadProblemViewModel();

        public string Title => "Usage";

        public string Scope => "All recorded usage, kept across restarts";

        public IReadOnlyList<IConnectionMeterViewModel> Connections => [];

        public IUsageRangeViewModel Range { get; } = new UsageRangeViewModel();

        public IReadOnlyList<IJobMeterViewModel> Jobs => [];

        public IReadOnlyList<IInterventionViewModel> Interventions => [];
    }
}

public sealed class UsageRangeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ANarrowRangeCaptionBreaksAfterItsDotNotBeforeItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageRangeViewModel(), Avalonia.Styling.ThemeVariant.Dark, 640, 480);
            var caption = view.Find<TextBlock>("Caption");
            var starts = new List<string>();

            for (var width = 260; width <= 640; width += 4)
            {
                view.Window.Width = width;
                view.Settle();
                var text = caption.Text ?? string.Empty;
                starts.AddRange(caption.TextLayout.TextLines.Skip(1).Select(line => text[line.FirstTextSourceIndex..].TrimStart()));
            }

            Assert.NotEmpty(starts);
            Assert.All(starts, start => Assert.False(start.StartsWith('·'), start));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheWindowIsChosenWithSegmentsAndItsDaysListUnderItsTotalAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageRangeViewModel());

            Assert.Equal((false, true, false), (view.HasClass("ShowToday", "selected"), view.HasClass("ShowWeek", "selected"), view.HasClass("ShowMonth", "selected")));
            Assert.Equal((Components.UI.States.Texts.Typeset("Since Sun 4 Oct, in this computer's time zone · newest first"), 7), (view.TextOf("Caption"), view.Find<ItemsControl>("Days").ItemCount));
            Assert.Contains("Tokens by type · Last 7 days", view.VisibleTexts);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingASegmentChoosesThatWindowAsync() =>
        ui.RunAsync(() =>
        {
            var range = new UsageRangeViewModel();
            var view = Screen.Show(range);

            view.Click("ShowMonth");
            view.Settle();

            Assert.Equal((true, false), (view.HasClass("ShowMonth", "selected"), view.HasClass("ShowWeek", "selected")));
        }, TestContext.Current.CancellationToken);
}

public sealed class UsageDayViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ADayShowsItsDateABarItsTokensAndCostWithTheTypesOnHoverAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageDayViewModel());

            Assert.Equal(("Sat 10 Oct · today", "620,400 tokens", "4.05 USD", 0.88), (view.TextOf("Day"), view.TextOf("Tokens"), view.TextOf("Cost"), view.Find<ProgressBar>("Share").Value));
            Assert.False(view.HasClass("Day", "secondary"));
        }, TestContext.Current.CancellationToken);
}

public sealed class ConnectionMeterViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AConnectionShowsItsTotalCostItsTokensCapsAndLimitWindowsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionMeterViewModel());

            Assert.Equal(("claude-work", "9.86 USD in total", 2), (view.TextOf("ConnectionName"), view.TextOf("Cost"), view.Find<ItemsControl>("Limits").ItemCount));
            Assert.Equal((false, false), (view.Shows("Unpriced"), view.Shows("NoLimits")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AConnectionWithoutLimitsSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionMeterViewModel("pi-local", "no cost", "0 tokens", []));

            Assert.True(view.Shows("NoLimits"));
        }, TestContext.Current.CancellationToken);
}

public sealed class LimitViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ALimitNearItsHoldTurnsAmberAndMarksTheThresholdAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel());

            Assert.Equal(("5-hour window", "88%", " used"), (view.TextOf("Window"), view.TextOf("UsedText"), view.TextOf("UsedWord")));
            Assert.Equal((0.88, 0.9), (view.Find<ProgressBar>("Used").Value, view.Find<ProgressBar>("Hold").Value));
            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<ProgressBar>("Used").Foreground).Color);
            Assert.Equal((true, true), (view.HasClass("UsedText", "attention"), view.HasClass("UsedWord", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnExpiredLimitSaysResetWithoutCallingItUsedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("7d", "7-day window", 0, "reset", "reset 2026-10-13 09:00, no reading since", 0.9, false));

            Assert.Equal(("reset", false), (view.TextOf("UsedText"), view.Shows("UsedWord")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitSaysWhenItResetsAsASentenceUnderItsBarAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel());

            Assert.Equal("Resets 2026-10-09 16:20", view.TextOf("Resets"));
            Assert.True(view.Find("Resets").TranslatePoint(default, view.Window)!.Value.Y >= view.Find("Used").TranslatePoint(new Point(0, view.Find("Used").Bounds.Height), view.Window)!.Value.Y);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitFarFromItsHoldStaysNeutralAndQuietAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("7d", "7-day window", 0.43, "43%", "resets 2026-10-13 09:00", 0.9, false));

            Assert.False(view.HasClass("UsedText", "attention"));
            Assert.Equal(Color.Parse("#A3A3AD"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<ProgressBar>("Used").Foreground).Color);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitPastItsHoldFillsTheBarWithoutOverflowingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("5h", "5-hour window", 1, "104%", "resets 16:20", 0.9, true));

            Assert.Equal((1d, "104%", true), (view.Find<ProgressBar>("Used").Value, view.TextOf("UsedText"), view.HasClass("UsedText", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitWithoutAHoldShowsNoMarkerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("5h", "5-hour window", 0.95, "95%", "resets 16:20", 0, false));

            Assert.Equal((false, false), (view.Shows("Hold"), view.HasClass("UsedText", "attention")));
        }, TestContext.Current.CancellationToken);
}

public sealed class UsageWindowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TokensByTypeStayFoldedUntilOpenedThenShowTheirSharesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageWindowViewModel());
            var folded = view.Shows("Turns");

            view.Find<Expander>("Disclosure").IsExpanded = true;
            view.Settle();
            var shares = view.Find<ShareBar>("Shares").Children.Select(child => child.Bounds.Width).ToList();

            Assert.False(folded);
            Assert.Equal(("Tokens by type · Today", "1,686,520 tokens", true), (view.TextOf("Label"), view.TextOf("Tokens"), view.Shows("Turns")));
            Assert.Contains("cache read 1,204,300", view.VisibleTexts);
            Assert.True(shares[2] > shares[0] && shares[0] > shares[1]);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task UnpricedReportsAreNotedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageWindowViewModel("Last 7 days", "14 USD", 1, 1, 1, 1, 1, "2 usage reports had no cost"));
            view.Find<Expander>("Disclosure").IsExpanded = true;
            view.Settle();

            Assert.Equal("2 usage reports had no cost", view.TextOf("Unpriced"));
        }, TestContext.Current.CancellationToken);
}

public sealed class JobMeterViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AJobShowsItsConnectionAndCostAgainstItsCapAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel());

            Assert.Equal(("Rate-limit POST /login", "claude-work", "1.12 USD", " / 3 USD", 0.37), (view.TextOf("Title"), view.TextOf("Connection"), view.TextOf("Cost"), view.TextOf("Cap"), view.Find<ProgressBar>("Used").Value));
            Assert.Equal((false, false), (view.Shows("Interventions"), view.HasClass("Used", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobAtItsCapTurnsAmberAndOneHeldSaysHowOftenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "claude-personal", "3.04 USD", 1, "3 USD", 2));

            Assert.Equal((true, "held 2 times"), (view.HasClass("Used", "attention"), view.TextOf("Interventions")));
            Assert.True(view.HasClass("Interventions", "attention"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobsLongTitleIsTrimmedWithAnEllipsisAndKeepsHowOftenItWasHeldInSightAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.SyncQueue, "[simulate: two-windows-spent] Rewrite the importer for the new CSV export format of the bank and keep the old one working", JobStatus.NeedsHelp, "claude-personal", "3.04 USD", 1, "3 USD", 2), Avalonia.Styling.ThemeVariant.Dark, 640, 200);
            var title = view.Find<TextBlock>("Title");

            Assert.Equal((true, true), (title.TextLayout.TextLines[0].HasCollapsed, view.Shows("Interventions")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task BesideTheDefaultSidebarAtTheNarrowestWindowAHeldJobsRowReadsItsTitleAndHowOftenItWasHeldAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.SyncQueue, "Rewrite the importer for the new CSV export format of the bank", JobStatus.NeedsHelp, "claude-personal", "12.48 USD", 0.83, "15 USD", 2), Avalonia.Styling.ThemeVariant.Dark, 412, 200);
            var held = view.Find<TextBlock>("Interventions");
            var title = view.Find<TextBlock>("Title");
            var given = (Held: held.Bounds.Width, Title: title.Bounds.Width);
            held.Measure(Size.Infinity);

            Assert.True(held.DesiredSize.Width <= given.Held && given.Title > 0, $"'held 2 times' needs {held.DesiredSize.Width:0} and is given {given.Held:0}, the title is given {given.Title:0}");
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobHeldOnlyOnceSaysOnceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "claude-personal", "3.04 USD", 1, "3 USD", 1));

            Assert.Equal("held once", view.TextOf("Interventions"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobWithoutACapHasNoBarAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.JpyRounding, "Fix JPY rounding", JobStatus.Running, "claude-work", "0.41 USD", 0, string.Empty, 0));

            Assert.Equal((false, false), (view.Shows("Used"), view.Shows("Cap")));
        }, TestContext.Current.CancellationToken);
}

public sealed class InterventionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnInterventionShowsWhenWhatAndWhyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignInterventionViewModel());

            Assert.Equal(("2026-10-09 14:52", "held: Stalled", "— silent for 600s, window 600s"), (view.TextOf("At"), view.TextOf("Reason"), view.TextOf("Detail")));
        }, TestContext.Current.CancellationToken);
}
