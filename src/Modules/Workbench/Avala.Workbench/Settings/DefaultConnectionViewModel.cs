using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Settings;

internal interface IDefaultConnectionViewModel
{
    IReadOnlyList<string> Choices { get; }

    string Draft { get; set; }

    string Saved { get; }

    bool IsChanged { get; }

    bool IsRecommended { get; }

    string Explanation { get; }

    string Error { get; }

    IAsyncRelayCommand SaveCommand { get; }

    event EventHandler<ConnectionCatalog>? Changed;

    void Show(ConnectionCatalog catalog);
}

[INotifyPropertyChanged]
internal sealed partial class DefaultConnectionViewModel(MachineSettings settings, IMessenger messenger) : IDefaultConnectionViewModel
{
    private readonly ObservableCollection<string> choices = [DefaultPhrases.Auto];

    public event EventHandler<ConnectionCatalog>? Changed;

    public IReadOnlyList<string> Choices => choices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(IsRecommended))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Draft { get; set; } = DefaultPhrases.Auto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Saved { get; private set; } = string.Empty;

    public bool IsChanged => !string.IsNullOrEmpty(Draft) && Draft != Saved;

    public bool IsRecommended => Draft == DefaultPhrases.Auto;

    [ObservableProperty]
    public partial string Explanation { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public void Show(ConnectionCatalog catalog)
    {
        var saved = DefaultPhrases.Choice(catalog);
        choices.ShowOnly([DefaultPhrases.Auto, .. catalog.Connections.Select(connection => connection.Name.Value)]);
        Saved = saved;
        Draft = saved.Length > 0 ? saved : DefaultPhrases.Auto;
        Explanation = DefaultPhrases.Explain(catalog);
    }

    [RelayCommand(CanExecute = nameof(IsChanged))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var chosen = Draft == DefaultPhrases.Auto ? Option<ConnectionName>.None : new ConnectionName(Draft);
        var changed = await settings.ChangeDefaultAsync(chosen, cancellationToken);
        Error = changed.Match(_ => string.Empty, DefaultPhrases.Refused);

        if (changed.TryGetValue(out var catalog, out _))
        {
            Show(catalog);
            Changed?.Invoke(this, catalog);
            messenger.Send(new DefaultConnectionChanged(catalog.DefaultMode, catalog.DefaultMode == DefaultMode.Fixed ? catalog.Default : Option<ConnectionName>.None));
        }
    }
}

internal static class DefaultPhrases
{
    public const string Auto = "Auto (most capacity)";

    public static string Choice(ConnectionCatalog catalog) =>
        catalog.Error.IsSome ? string.Empty
        : catalog.DefaultMode == DefaultMode.Auto ? Auto
        : catalog.Default.Match(name => name.Value, () => string.Empty);

    public static string Explain(ConnectionCatalog catalog) =>
        catalog.Error.Match(
            error => error == ConnectionError.UnknownDefault
                ? "connections.json names a default connection this machine no longer has, so no job can start. Choose the default again to repair it."
                : $"connections.json is rejected ({error}), so no job can start. Edit the file to fix it.",
            () => catalog.DefaultMode == DefaultMode.Auto
                ? "Recommended. A job that names no connection, in a repository that names none, runs on the connection with the most capacity left."
                : catalog.Default.Match(
                    name => $"A job that names no connection, in a repository that names none, runs on {name.Value}, even near its limit.",
                    () => "No connection is available yet."));

    public static string Refused(ConnectionError error) => error switch
    {
        ConnectionError.UnknownConnection => "That connection is no longer on this machine.",
        ConnectionError.InvalidName => "A connection named auto cannot be the default: auto means choosing by capacity.",
        ConnectionError.Unwritable => "connections.json could not be written.",
        _ => $"connections.json is rejected ({error}): edit the file to fix it.",
    };
}
