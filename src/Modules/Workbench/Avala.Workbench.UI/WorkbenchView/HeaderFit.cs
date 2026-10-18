using Avalonia.Controls;

namespace Avala.Workbench.UI;

internal sealed class HeaderFit
{
    private const double TitleRoom = 160;

    private readonly HeaderParts parts;
    private double fullChips;
    private double compactChips;
    private double pillWidth;

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

    private void Remember()
    {
        if (!Is("quiet"))
        {
            pillWidth = parts.Pill.DesiredSize.Width;
        }

        if (Is("stacked"))
        {
            return;
        }

        if (Is("compact"))
        {
            compactChips = parts.Chips.DesiredSize.Width;
        }
        else
        {
            fullChips = parts.Chips.DesiredSize.Width;
        }
    }
}

internal sealed record HeaderParts(Border Header, Grid Row, Control Title, Control Chips, Control Pill);
