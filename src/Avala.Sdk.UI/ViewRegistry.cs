using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Sdk.UI;

public sealed class ViewRegistry : IViewRegistrar, IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> factories = [];

    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new() =>
        factories[typeof(TViewModel)] = static () => new TView();

    public bool Match(object? data) => data is not null && FactoryFor(data.GetType()) is not null;

    public Control? Build(object? param) => param is not null ? FactoryFor(param.GetType())?.Invoke() : null;

    private Func<Control>? FactoryFor(Type type) =>
        factories.TryGetValue(type, out var exact)
            ? exact
            : factories.FirstOrDefault(entry => entry.Key.IsAssignableFrom(type)).Value;
}
