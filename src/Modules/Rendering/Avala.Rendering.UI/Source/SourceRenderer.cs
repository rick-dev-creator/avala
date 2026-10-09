using Avala.Rendering.Highlighting;
using Avalonia.Controls.Documents;

namespace Avala.Rendering.UI.Source;

internal static class SourceRenderer
{
    public static Func<string, IEnumerable<Inline>> Painting(Func<string, IReadOnlyList<SourceToken>> highlight) =>
        text => highlight(text).Select(token => SourceColors.Paint(new Run(token.Text), token.Kind));
}
