using Avalonia.Controls;

namespace Avala.Sdk.UI;

public interface IViewRegistrar
{
    void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new();
}
