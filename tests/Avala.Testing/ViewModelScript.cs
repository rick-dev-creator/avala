using System.ComponentModel;
using System.Windows.Input;
using Xunit;

namespace Avala.Testing;

public static class ViewModelScript
{
    public static ViewModelScript<TViewModel> Given<TViewModel>(TViewModel viewModel)
        where TViewModel : class => new(viewModel);
}

public sealed class ViewModelScript<TViewModel>
    where TViewModel : class
{
    private readonly List<string> notified = [];

    internal ViewModelScript(TViewModel viewModel)
    {
        ViewModel = viewModel;

        if (viewModel is INotifyPropertyChanged observable)
        {
            observable.PropertyChanged += (_, change) => notified.Add(change.PropertyName ?? string.Empty);
        }
    }

    public TViewModel ViewModel { get; }

    public IReadOnlyList<string> Notified => notified;

    public ViewModelScript<TViewModel> When(Action<TViewModel> action)
    {
        action(ViewModel);

        return this;
    }

    public ViewModelScript<TViewModel> Invoke(string command) => Invoke(command, null);

    public ViewModelScript<TViewModel> Invoke(string command, object? parameter)
    {
        var bound = Read<ICommand>(command);
        Assert.True(bound.CanExecute(parameter), $"{command} cannot run now.");
        bound.Execute(parameter);

        return this;
    }

    public bool CanInvoke(string command) => Read<ICommand>(command).CanExecute(null);

    public T Read<T>(string property) =>
        Assert.IsAssignableFrom<T>(typeof(TViewModel).GetProperty(property)?.GetValue(ViewModel)
            ?? ViewModel.GetType().GetProperty(property)?.GetValue(ViewModel));

    public ViewModelScript<TViewModel> Then(Action<TViewModel> assertion)
    {
        assertion(ViewModel);

        return this;
    }

    public ViewModelScript<TViewModel> ThenNotified(params string[] properties)
    {
        Assert.Superset(properties.ToHashSet(StringComparer.Ordinal), notified.ToHashSet(StringComparer.Ordinal));

        return this;
    }
}
