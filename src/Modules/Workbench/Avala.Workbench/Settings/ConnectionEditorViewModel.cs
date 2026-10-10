using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Workbench.Machine;
using Avala.Workbench.ModelChoices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IConnectionEditorViewModel
{
    bool IsOpen { get; }

    string Title { get; }

    string Name { get; set; }

    IReadOnlyList<string> Providers { get; }

    int Provider { get; set; }

    IReadOnlyList<string> Sources { get; }

    int Source { get; set; }

    string Reference { get; set; }

    bool NeedsReference { get; }

    string ReferenceHint { get; }

    IModelPickerViewModel Models { get; }

    string Error { get; }

    string Removing { get; }

    IRelayCommand NewCommand { get; }

    IAsyncRelayCommand SaveCommand { get; }

    IRelayCommand CancelCommand { get; }

    IAsyncRelayCommand RemoveCommand { get; }

    IRelayCommand KeepCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConnectionEditorViewModel(MachineSettings settings, ModelPickerViewModel models) : IConnectionEditorViewModel
{
    private ConnectionCatalog catalog = new(ConnectionFileStatus.Absent, Option<ConnectionError>.None, [], Option<ConnectionName>.None);
    private Option<ConnectionName> editing;
    private int offers;

    public IModelPickerViewModel Models => models;

    public ModelPickerViewModel Picker => models;

    public Task Offering { get; private set; } = Task.CompletedTask;

    public event EventHandler<ConnectionEdited>? Edited;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Providers { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int Provider { get; set; } = -1;

    [ObservableProperty]
    public partial IReadOnlyList<string> Sources { get; private set; } = [ConnectionPhrases.OwnLogin];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReference), nameof(ReferenceHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int Source { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Reference { get; set; } = string.Empty;

    public bool NeedsReference => Source > 0;

    public string ReferenceHint => ConnectionPhrases.ReferenceHint(SourceName);

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial string Removing { get; private set; } = string.Empty;

    private string SourceName => Source > 0 && Source <= catalog.Sources.Count ? catalog.Sources[Source - 1] : string.Empty;

    public void Show(ConnectionCatalog shown)
    {
        catalog = shown;
        Providers = [.. shown.Providers.Select(provider => $"{provider.Name} · {provider.Id}")];
        Sources = [ConnectionPhrases.OwnLogin, .. shown.Sources.Select(ConnectionPhrases.Source)];
    }

    public void Edit(DeclaredConnection connection)
    {
        Open(connection.Name, $"Edit {connection.Name.Value}");
        Name = connection.Name.Value;
        Provider = catalog.Providers.ToList().FindIndex(provider => provider.Id == connection.Provider);
        Source = connection.Source.Match(source => catalog.Sources.ToList().IndexOf(source) + 1, () => 0);
        Reference = connection.Reference.Match(reference => reference, () => string.Empty);
        Offering = OfferAsync(CancellationToken.None);
    }

    public void AskToRemove(ConnectionName connection)
    {
        IsOpen = false;
        Error = string.Empty;
        Removing = connection.Value;
    }

    [RelayCommand]
    private void New()
    {
        Open(Option<ConnectionName>.None, "New connection");
        Name = string.Empty;
        Provider = catalog.Providers.Count > 0 ? 0 : -1;
        Source = 0;
        Reference = string.Empty;
        Offering = OfferAsync(CancellationToken.None);
    }

    partial void OnProviderChanged(int value)
    {
        if (IsOpen)
        {
            Offering = OfferAsync(CancellationToken.None);
        }
    }

    private async Task OfferAsync(CancellationToken cancellationToken)
    {
        var ticket = ++offers;

        if (Provider < 0 || Provider >= catalog.Providers.Count)
        {
            models.Hide(string.Empty);
            return;
        }

        var offered = (await settings.CapabilitiesAsync(editing, Edit(Option<ModelChoice>.None), cancellationToken))
            .Match(found => found.Get<OffersModels>(), _ => Option<OffersModels>.None);

        if (ticket == offers)
        {
            offered.Match<Action>(
                found => () => models.Offer(found, ModelPhrases.Connection, found.DefaultChoice(), ModelPhrases.ConnectionNote),
                () => () => models.Hide(string.Empty))();
        }
    }

    private ConnectionEdit Edit(Option<ModelChoice> model) =>
        new(new ConnectionName(Name.Trim()), catalog.Providers[Provider].Id)
        {
            Credential = NeedsReference ? new CredentialReference(SourceName, Reference.Trim()) : Option<CredentialReference>.None,
            Model = model,
        };

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var edit = Edit(models.CanChoose ? models.Chosen : Option<ModelChoice>.None);
        var saved = await settings.DeclareAsync(editing, edit, cancellationToken);
        Error = saved.Match(_ => string.Empty, ConnectionPhrases.Refused);

        if (saved.TryGetValue(out var changed, out _))
        {
            IsOpen = false;
            Report(changed, ConnectionPhrases.Saved(editing, edit.Name));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        Error = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync(CancellationToken cancellationToken)
    {
        var name = new ConnectionName(Removing);
        var removed = await settings.RemoveAsync(name, cancellationToken);
        Error = removed.Match(_ => string.Empty, ConnectionPhrases.Refused);
        Removing = string.Empty;

        if (removed.TryGetValue(out var changed, out _))
        {
            Report(changed, ConnectionPhrases.Removed(name));
        }
    }

    [RelayCommand]
    private void Keep() => Removing = string.Empty;

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(Name) && Provider >= 0 && Provider < catalog.Providers.Count && (!NeedsReference || !string.IsNullOrWhiteSpace(Reference));

    private bool CanRemove() => Removing.Length > 0;

    private void Open(Option<ConnectionName> connection, string title)
    {
        editing = connection;
        Title = title;
        Error = string.Empty;
        Removing = string.Empty;
        IsOpen = true;
    }

    private void Report(ConnectionCatalog changed, string notice)
    {
        var dropped = catalog.Connections
            .Where(known => known.Origin == ConnectionOrigin.Implicit && changed.Connections.All(kept => kept.Name != known.Name))
            .Select(known => known.Name.Value)
            .ToList();
        Show(changed);
        Edited?.Invoke(this, new ConnectionEdited(changed, notice + ConnectionPhrases.Dropped(dropped)));
    }
}

internal sealed record ConnectionEdited(ConnectionCatalog Catalog, string Notice);

internal static class ConnectionPhrases
{
    public const string OwnLogin = "The provider's own login";

    public static string Source(string source) => source switch
    {
        "login" => "login · a configuration folder",
        "apiKey" => "apiKey · an environment variable",
        _ => source,
    };

    public static string ReferenceHint(string source) => source switch
    {
        "login" => "The folder the harness keeps this login in.",
        "apiKey" => "The name of the environment variable that holds the key, such as TEAM_API_KEY. Avala stores the name only and never reads or writes the key here.",
        "" => "The harness uses its own login on this computer.",
        _ => "The reference this credential source expects.",
    };

    public static string Saved(Option<ConnectionName> replaced, ConnectionName saved) =>
        replaced.Match(
            old => old == saved
                ? $"Saved {saved.Value} in connections.json. New sessions on it use the change."
                : $"Renamed {old.Value} to {saved.Value} in connections.json. A repository whose .avala/jobs.json names {old.Value} must be changed too.",
            () => $"Added {saved.Value} to connections.json. New jobs can run on it now.");

    public static string Dropped(IReadOnlyList<string> implicitConnections) =>
        implicitConnections.Count == 0
            ? string.Empty
            : $" A file that declares connections replaces the implicit ones, so {string.Join(", ", implicitConnections)} is no longer offered.";

    public static string Removed(ConnectionName removed) =>
        $"Removed {removed.Value} from connections.json. Jobs already running on it go on; new jobs cannot choose it.";

    public static string Refused(ConnectionError error) => error switch
    {
        ConnectionError.InvalidName => "A name is letters, digits, '-', '_' or '.', starts with a letter or digit, and is not auto.",
        ConnectionError.DuplicateName => "Another connection already has that name.",
        ConnectionError.UnknownProvider => "No harness on this machine provides that.",
        ConnectionError.UnknownSource => "This machine has no such credential source.",
        ConnectionError.MissingReference => "Say where the credential is.",
        ConnectionError.UnknownConnection => "That connection is not declared in connections.json: only declared connections can be changed here.",
        ConnectionError.RemovesTheDefault => "This is the default connection: choose another default first.",
        ConnectionError.UnofferedModel => "The harness does not offer this model on this connection.",
        ConnectionError.UnofferedEffort => "The harness does not offer this effort level on this connection.",
        ConnectionError.Unwritable => "connections.json could not be written.",
        _ => $"connections.json is rejected ({error}): open it to fix it.",
    };
}
