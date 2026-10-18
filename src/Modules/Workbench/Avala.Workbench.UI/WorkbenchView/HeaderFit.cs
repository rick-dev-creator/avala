using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Avala.Workbench.UI;

internal sealed class HeaderFit
{
    private const double TitleRoom = 160;

    private readonly HeaderParts parts;
    private double fullChips;
    private double compactChips;
    private double pillWidth;
    private string shown = string.Empty;

    private HeaderFit(HeaderParts parts) => this.parts = parts;

    public static void Attach(HeaderParts parts)
    {
        var fit = new HeaderFit(parts);
        parts.Row.SizeChanged += (_, _) => fit.Fit();
        parts.Chips.SizeChanged += (_, _) => fit.Fit();
        parts.Pill.SizeChanged += (_, _) => fit.Fit();
    }

    private bool Is(string name) => parts.Header.Classes.Contains(name);

    private void Fit()
    {
        var available = parts.Row.Bounds.Width;

        if (available <= 0)
        {
            return;
        }

        Forget();
        Remember();
        var needed = TitleRoom + parts.Row.Children.Where(child => child != parts.Title && child != parts.Chips && child != parts.Pill && child.IsVisible).Sum(child => child.DesiredSize.Width);
        var stackedCompact = fullChips > available;
        var (compact, stacked, quiet) =
            needed + pillWidth + fullChips <= available ? (false, false, false)
            : needed + pillWidth + compactChips <= available ? (true, false, false)
            : needed + pillWidth <= available ? (stackedCompact, true, false)
            : (stackedCompact, true, true);

        parts.Header.Classes.Set("compact", compact);
        parts.Header.Classes.Set("stacked", stacked);
        parts.Header.Classes.Set("quiet", quiet);
    }

    private void Forget()
    {
        var chips = string.Join(',', parts.Chips.GetRealizedContainers().Select((chip, index) => chip.DesiredSize.Width > 0 ? index : -1).Where(index => index >= 0));
        var words = parts.Chips.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => !text.Classes.Contains("chip-label") && text.IsEffectivelyVisible)
            .Sum(text => text.Text?.Length ?? 0);
        var now = $"{chips}|{words}";

        if (now != shown)
        {
            shown = now;
            fullChips = 0;
            compactChips = 0;
        }
    }

    private void Remember()
    {
        if (!Is("quiet") && parts.Pill.IsMeasureValid)
        {
            pillWidth = parts.Pill.DesiredSize.Width;
        }

        if (Is("stacked") || Is("compact") || !parts.Chips.IsMeasureValid)
        {
            return;
        }

        var labels = parts.Chips.GetVisualDescendants().OfType<TextBlock>()
            .Where(label => label.Classes.Contains("chip-label") && label.IsEffectivelyVisible && label.DesiredSize.Width > 0)
            .Sum(label => label.DesiredSize.Width + (label.GetVisualParent() is StackPanel row ? row.Spacing : 0));
        fullChips = parts.Chips.DesiredSize.Width;
        compactChips = fullChips - labels;
    }
}

internal sealed record HeaderParts(Border Header, Grid Row, Control Title, ItemsControl Chips, Control Pill);
