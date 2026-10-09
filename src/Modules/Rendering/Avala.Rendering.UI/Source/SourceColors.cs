using Avala.Rendering.Highlighting;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace Avala.Rendering.UI.Source;

internal static class SourceColors
{
    public static Run Paint(Run run, TokenKind kind)
    {
        run.Classes.Add(kind.ToString().ToLowerInvariant());

        if (Brush(kind) is { Length: > 0 } brush)
        {
            run.Bind(TextElement.ForegroundProperty, run.GetResourceObservable(brush));
        }

        return run;
    }

    private static string Brush(TokenKind kind) => kind switch
    {
        TokenKind.Keyword or TokenKind.Name => "AccentBrush",
        TokenKind.Attribute => "TextSecondaryBrush",
        TokenKind.Text => "AccentHoverBrush",
        TokenKind.Comment or TokenKind.Operator => "TextTertiaryBrush",
        _ => string.Empty,
    };
}
