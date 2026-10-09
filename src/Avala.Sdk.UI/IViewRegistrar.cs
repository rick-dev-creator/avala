using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Sdk.UI;

public interface IViewRegistrar
{
    void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new();

    void Register(IDataTemplate template);
}
