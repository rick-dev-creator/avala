using Avala.Sdk;

namespace Avala.Workbench.Settings;

internal interface ISettingsViewModel
{
    string Title { get; }

    IRepositorySettingsViewModel Repository { get; }

    IMachineSettingsViewModel Machine { get; }
}

internal sealed class SettingsViewModel(IRepositorySettingsViewModel repository, IMachineSettingsViewModel machine) : ISettingsViewModel, IPage, IActivatable
{
    public string Title => "Settings";

    public IRepositorySettingsViewModel Repository { get; } = repository;

    public IMachineSettingsViewModel Machine { get; } = machine;

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
