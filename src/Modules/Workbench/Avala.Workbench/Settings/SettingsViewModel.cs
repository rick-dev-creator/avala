using Avala.Sdk;

namespace Avala.Workbench.Settings;

internal interface ISettingsViewModel
{
    string Title { get; }

    IRepositorySettingsViewModel Repository { get; }

    IMachineSettingsViewModel Machine { get; }

    IAppearanceViewModel Appearance { get; }

    IAboutViewModel About { get; }
}

internal sealed class SettingsViewModel(
    IRepositorySettingsViewModel repository,
    IMachineSettingsViewModel machine,
    IAppearanceViewModel appearance,
    IAboutViewModel about) : ISettingsViewModel, IPage, IActivatable
{
    public string Title => "Settings";

    public string Icon => "IconSettings";

    public IRepositorySettingsViewModel Repository { get; } = repository;

    public IMachineSettingsViewModel Machine { get; } = machine;

    public IAppearanceViewModel Appearance { get; } = appearance;

    public IAboutViewModel About { get; } = about;

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate() => Loading = LoadAsync(CancellationToken.None);

    public void Deactivate()
    {
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        About.Refresh();
        await Machine.LoadAsync(cancellationToken);
        await Appearance.LoadAsync(cancellationToken);
        await Repository.LoadAsync(cancellationToken);
    }
}
