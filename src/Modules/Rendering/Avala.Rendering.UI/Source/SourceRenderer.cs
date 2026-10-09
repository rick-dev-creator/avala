using Avala.Components.UI.Canvases;
using Avala.Rendering.Highlighting;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace Avala.Rendering.UI.Source;

internal sealed class SourceRenderer(string rendered, string explanation, Func<string, IReadOnlyList<SourceToken>> highlight) : ICanvasRenderer
{
    public bool Renders(string mediaType) => mediaType == rendered;

    public Option<Control> Render(string content) => CanvasSource.Show(explanation, content, Highlighted);

    private IEnumerable<Inline> Highlighted(string text) =>
        highlight(text).Select(token => SourceColors.Paint(new Run(token.Text), token.Kind));
}
