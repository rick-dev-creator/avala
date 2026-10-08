using Avala.Sdk.UI;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Host.Composition;

internal sealed class ViewRegistry : IViewRegistrar, IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> factories = [];

    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new() =>
        factories[typeof(TViewModel)] = static () => new TView();

    public bool Match(object? data) => data is not null && factories.ContainsKey(data.GetType());

    public Control? Build(object? param) =>
        param is not null && factories.TryGetValue(param.GetType(), out var factory)
            ? factory()
            : null;
}
