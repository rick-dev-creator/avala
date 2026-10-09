using Avala.Sdk;
using Avalonia.Controls;

namespace Avala.Components.UI.Canvases;

public interface ICanvasRenderer
{
    bool Renders(string mediaType);

    Option<Control> Render(string content);
}
