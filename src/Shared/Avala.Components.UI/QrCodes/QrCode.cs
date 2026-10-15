using Avala.Components.QrCodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.QrCodes;

public sealed class QrCode : Control
{
    public static readonly StyledProperty<QrModules> ModulesProperty =
        AvaloniaProperty.Register<QrCode, QrModules>(nameof(Modules), QrModules.None);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<QrCode, IBrush?>(nameof(Foreground));

    static QrCode() => AffectsRender<QrCode>(ModulesProperty, ForegroundProperty);

    public QrModules Modules
    {
        get => GetValue(ModulesProperty);
        set => SetValue(ModulesProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var modules = Modules;

        if (modules.IsEmpty || Foreground is not { } ink)
        {
            return;
        }

        var cell = Math.Floor(Math.Min(Bounds.Width, Bounds.Height) / modules.Size * 4) / 4;
        var origin = new Point((Bounds.Width - (cell * modules.Size)) / 2, (Bounds.Height - (cell * modules.Size)) / 2);

        for (var row = 0; row < modules.Size; row++)
        {
            for (var column = 0; column < modules.Size; column++)
            {
                if (modules.IsDark(row, column))
                {
                    context.FillRectangle(ink, new Rect(origin.X + (column * cell), origin.Y + (row * cell), cell, cell));
                }
            }
        }
    }
}
