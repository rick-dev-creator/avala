using Avala.Preview.Surfaces;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Preview.UI.Surfaces;

internal sealed class NativeSurfaceTemplate : IDataTemplate
{
    public bool Match(object? data) => data is NativeSurface { Embedding: SurfaceEmbedding.Embedded };

    public Control? Build(object? param)
    {
        if (param is not NativeSurface surface)
        {
            return null;
        }

        var view = new NativeWebView();
        surface.Attach(view);

        return view;
    }
}
