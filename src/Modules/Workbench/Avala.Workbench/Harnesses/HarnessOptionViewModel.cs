using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Workbench.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Harnesses;

internal interface IHarnessOptionViewModel
{
    string Name { get; }

    string Monogram { get; }

    bool IsAuto { get; }

    string Hint { get; }

    bool IsAttention { get; }

    bool IsChosen { get; }

    bool IsAvailable { get; }

    IRelayCommand ChooseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class HarnessOptionViewModel(string name, Option<ConnectionName> connection, Action<Option<ConnectionName>> choose) : IHarnessOptionViewModel
{
    public string Name { get; } = name;

    public Option<ConnectionName> Connection { get; } = connection;

    public string Monogram { get; } = connection.Match(found => AccountPhrases.Monogram(found.Value), () => string.Empty);

    public bool IsAuto => Connection.IsNone;

    [ObservableProperty]
    public partial string Hint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAttention { get; private set; }

    [ObservableProperty]
    public partial bool IsChosen { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    public partial bool IsAvailable { get; private set; } = true;

    public void Show(string hint, bool attention, bool chosen, bool available = true)
    {
        Hint = hint;
        IsAttention = attention;
        IsChosen = chosen;
        IsAvailable = available;
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private void Choose() => choose(Connection);
}
