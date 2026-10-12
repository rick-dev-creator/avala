using Avala.Components.Privacy;
using Avala.Workbench.Accounts;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Onboarding;

internal interface ICodeStepViewModel : IOnboardingStep
{
    Personal Folder { get; }

    ISourceControlViewModel Accounts { get; }

    IAsyncRelayCommand ChangeFolderCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class CodeStepViewModel(ProjectsPlace place, ISourceControlViewModel accounts) : ICodeStepViewModel
{
    [ObservableProperty]
    public partial Personal Folder { get; private set; } = Personal.None;

    public ISourceControlViewModel Accounts => accounts;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Folder = Personal.Path((await place.ReadAsync(cancellationToken)).Folder);
        await accounts.LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ChangeFolderAsync(CancellationToken cancellationToken) =>
        Folder = (await place.ChangeAsync(cancellationToken)).Match(Personal.Path, () => Folder);
}
