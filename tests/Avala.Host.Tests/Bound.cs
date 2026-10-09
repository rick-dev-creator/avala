using System.Collections;
using System.Windows.Input;
using Avala.Sdk.Presentation;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Host.Tests;

internal readonly record struct Bound(object Target)
{
    public Bound this[string property] =>
        new(Target.GetType().GetProperty(property)?.GetValue(Target)
            ?? throw new InvalidOperationException($"{Kind}.{property} is not bound to anything."));

    public string Kind => Target.GetType().Name;

    public IReadOnlyList<Bound> Items => [.. ((IEnumerable)Target).Cast<object>().Select(item => new Bound(item))];

    public string Text => Target.ToString() ?? string.Empty;

    public bool Has(string property) => Target.GetType().GetProperty(property)?.GetValue(Target) is not null;

    public T Value<T>() => (T)Target;

    public void Set(string property, object value) => Target.GetType().GetProperty(property)!.SetValue(Target, value);

    public void Execute(string command, object? parameter = null) => ((ICommand)this[command].Target).Execute(parameter);

    public Task ExecuteAsync(string command, object? parameter = null) => ((IAsyncRelayCommand)this[command].Target).ExecuteAsync(parameter);

    public IPresentation Presentation => (IPresentation)Target;
}
