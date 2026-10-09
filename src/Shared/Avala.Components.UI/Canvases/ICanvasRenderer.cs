using Avala.Sdk;
using Avalonia.Controls;

namespace Avala.Components.UI.Canvases;

public interface ICanvasRenderer
{
    string MediaType { get; }

    Option<Control> Render(string content);
}
