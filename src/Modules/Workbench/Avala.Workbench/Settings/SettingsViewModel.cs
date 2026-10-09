using Avala.Sdk;

namespace Avala.Workbench.Settings;

internal sealed class SettingsViewModel(RepositorySettingsViewModel repository, MachineSettingsViewModel machine) : IPage, IActivatable
{
    public string Title => "Settings";

    public RepositorySettingsViewModel Repository { get; } = repository;

    public MachineSettingsViewModel Machine { get; } = machine;

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate() => Loading = LoadAsync(CancellationToken.None);

    public void Deactivate()
    {
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await Machine.LoadAsync(cancellationToken);
        await Repository.LoadAsync(cancellationToken);
    }
}
