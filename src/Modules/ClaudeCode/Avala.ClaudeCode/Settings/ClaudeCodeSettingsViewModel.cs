using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.ClaudeCode.Settings;

internal interface IClaudeCodeSettingsViewModel
{
    IReadOnlyList<IClaudeCodeLoginViewModel> Logins { get; }

    bool HasLogins { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ClaudeCodeSettingsViewModel(IConnections connections, IUiDispatcher ui) : IClaudeCodeSettingsViewModel, IActivatable
{
    private readonly ObservableCollection<IClaudeCodeLoginViewModel> logins = [];

    public IReadOnlyList<IClaudeCodeLoginViewModel> Logins => logins;

    [ObservableProperty]
    public partial bool HasLogins { get; private set; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate() => Loading = LoadAsync(CancellationToken.None);

    public void Deactivate()
    {
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var catalog = await connections.CatalogAsync(cancellationToken);
        var found = catalog.Connections.Where(connection => connection.Provider == ClaudeCodeProvider.Id && connection.Problem.IsNone).ToList();

        await ui.InvokeAsync(
            () =>
            {
                logins.Clear();

                foreach (var connection in found)
                {
                    logins.Add(new ClaudeCodeLoginViewModel(connection, connections));
                }

                HasLogins = logins.Count > 0;
            },
            cancellationToken);
    }
}
