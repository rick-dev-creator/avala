using Avala.Sdk.UI;
using Avalonia.Controls.Documents;

namespace Avala.Components.UI.Canvases;

public static class CanvasRenderers
{
    extension(IViewRegistrar views)
    {
        public IViewRegistrar AddCanvasRenderer(ICanvasRenderer renderer)
        {
            views.Register(new CanvasRendererTemplate(renderer));

            return views;
        }

        public IViewRegistrar AddCanvasSource(string mediaType, Func<string, IEnumerable<Inline>> highlight)
        {
            views.Register(new CanvasSourceTemplate(mediaType, highlight));

            return views;
        }
    }
}
