using Avala.Components.Canvases;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Components.UI.Canvases;

public sealed class CanvasRendererTemplate(ICanvasRenderer renderer) : IDataTemplate
{
    public string MediaType => renderer.MediaType;

    public bool Match(object? data) =>
        data is CanvasRendering { IsOffered: true } rendering && string.Equals(rendering.Essence, renderer.MediaType, StringComparison.OrdinalIgnoreCase);

    public Control? Build(object? param) =>
        param is CanvasRendering rendering
            ? renderer.Render(rendering.Content).Match<Control?>(control => control, () => null)
            : null;
}
