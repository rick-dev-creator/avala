using Avala.Components.Canvases;
using Avala.Sdk.UI;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Components.UI.Canvases;

public static class CanvasRenderers
{
    extension(IViewRegistrar views)
    {
        public IViewRegistrar AddCanvasRenderer(ICanvasRenderer renderer)
        {
            views.Register(new RendererTemplate(renderer));

            return views;
        }
    }

    private sealed class RendererTemplate(ICanvasRenderer renderer) : IDataTemplate
    {
        public bool Match(object? data) => data is CanvasRendering rendering && renderer.Renders(rendering.Essence);

        public Control? Build(object? param) =>
            param is CanvasRendering rendering
                ? renderer.Render(rendering.Content).Match<Control?>(control => control, () => null)
                : null;
    }
}
