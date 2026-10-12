using System.Collections.ObjectModel;
using Avala.Components.Privacy;
using Avala.Sdk;
using Avala.Workbench.Submitting;
using Avala.Workbench.Presenting;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal interface IRepositoryFieldViewModel
{
    IReadOnlyList<string> Repositories { get; }

    string Repository { get; set; }

    string Problem { get; }

    string Offer { get; }

    PersonalPhrase Notice { get; }

    INewRepositoryViewModel Creation { get; }

    ICloneSheetViewModel Cloning { get; }

    IAsyncRelayCommand ChooseCommand { get; }

    IAsyncRelayCommand MakeRepositoryCommand { get; }
}

internal sealed record RepositoryStarts(NewRepositoryViewModel Creation, CloneSheetViewModel Cloning);

[INotifyPropertyChanged]
internal sealed partial class RepositoryFieldViewModel : IRepositoryFieldViewModel
{
    private readonly ObservableCollection<string> known = [];
    private readonly IFolderPicker picker;
    private readonly IRepositories repositories;
    private readonly RecentRepositories recent;
    private readonly NewRepositoryViewModel creation;
    private readonly CloneSheetViewModel cloning;
    private int checks;

    public RepositoryFieldViewModel(IFolderPicker picker, IRepositories repositories, RecentRepositories recent, RepositoryStarts starts)
    {
        this.picker = picker;
        this.repositories = repositories;
        this.recent = recent;
        creation = starts.Creation;
        cloning = starts.Cloning;
        creation.Created += (_, created) => Arrive(created.Path, created.Notice);
        cloning.Cloned += (_, cloned) => Arrive(cloned.Path, RepositoryPhrases.Cloned(cloned));
    }

    public IReadOnlyList<string> Repositories => known;

    public INewRepositoryViewModel Creation => creation;

    public NewRepositoryViewModel Creating => creation;

    public ICloneSheetViewModel Cloning => cloning;

    public CloneSheetViewModel Clone => cloning;

    public Task Checking { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial string Repository { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Offer { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial PersonalPhrase Notice { get; private set; } = PersonalPhrase.Empty;

    public void ShowKnown() =>
        known.ShowOnly(recent.Recent());

    partial void OnRepositoryChanged(string value)
    {
        Notice = PersonalPhrase.Empty;
        Checking = CheckAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ChooseAsync(CancellationToken cancellationToken)
    {
        var chosen = await picker.PickAsync(RepositoryPhrases.ChooseTitle, Repository, cancellationToken);
        chosen.Match<Action>(found => () => Repository = found, () => () => { })();
        await Checking;
    }

    [RelayCommand(CanExecute = nameof(CanMakeRepository))]
    private async Task MakeRepositoryAsync(CancellationToken cancellationToken)
    {
        var made = await repositories.InitializeAsync(Repository, cancellationToken);

        if (!made.TryGetValue(out var repository, out var failure))
        {
            Problem = RepositoryPhrases.Failure(failure);

            return;
        }

        Checking = CheckAsync(cancellationToken);
        await Checking;
        Notice = RepositoryPhrases.Made(repository);
    }

    private bool CanMakeRepository() => Offer.Length > 0;

    private void Arrive(string repository, PersonalPhrase notice)
    {
        Repository = repository;
        Notice = notice;
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        var ticket = ++checks;
        var inspected = string.IsNullOrWhiteSpace(Repository)
            ? RepositoryState.Ready
            : await repositories.InspectAsync(Repository, cancellationToken);

        if (ticket == checks)
        {
            Problem = RepositoryPhrases.Problem(inspected);
            Offer = RepositoryPhrases.Offer(inspected);
            MakeRepositoryCommand.NotifyCanExecuteChanged();
        }
    }
}
