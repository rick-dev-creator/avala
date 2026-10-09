using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Sdk.UI;

public sealed class ViewRegistry : IViewRegistrar, IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> factories = [];
    private readonly List<IDataTemplate> templates = [];

    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new() =>
        factories[typeof(TViewModel)] = static () => new TView();

    public void Register(IDataTemplate template) => templates.Insert(0, template);

    public bool Match(object? data) => data is not null && (FactoryFor(data.GetType()) is not null || TemplateFor(data) is not null);

    public Control? Build(object? param) =>
        param is null ? null
        : FactoryFor(param.GetType()) is { } factory ? factory()
        : TemplateFor(param)?.Build(param);

    private Func<Control>? FactoryFor(Type type) =>
        factories.TryGetValue(type, out var exact)
            ? exact
            : factories.FirstOrDefault(entry => entry.Key.IsAssignableFrom(type)).Value;

    private IDataTemplate? TemplateFor(object data) => templates.FirstOrDefault(template => template.Match(data));
}
