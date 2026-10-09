using Avala.Components.Canvases;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;

namespace Avala.Components.UI.Canvases;

public sealed class CanvasSourceTemplate(string mediaType, Func<string, IEnumerable<Inline>> highlight) : IDataTemplate
{
    public string MediaType => mediaType;

    public bool Match(object? data) =>
        data is CanvasRendering { IsOffered: false } rendering && string.Equals(rendering.Essence, mediaType, StringComparison.OrdinalIgnoreCase);

    public Control? Build(object? param) =>
        param is CanvasRendering rendering ? CanvasSource.Show(CanvasSource.NotOffered(rendering), rendering.Content, highlight) : null;
}
