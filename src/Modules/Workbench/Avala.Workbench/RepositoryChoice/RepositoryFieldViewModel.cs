using System.Collections.ObjectModel;
using Avala.Sdk;
using Avala.Workbench.Board;
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

    string Notice { get; }

    INewRepositoryViewModel Creation { get; }

    IAsyncRelayCommand ChooseCommand { get; }

    IAsyncRelayCommand MakeRepositoryCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RepositoryFieldViewModel : IRepositoryFieldViewModel
{
    private readonly ObservableCollection<string> known = [];
    private readonly IFolderPicker picker;
    private readonly IRepositories repositories;
    private readonly JobBoard board;
    private readonly NewRepositoryViewModel creation;
    private int checks;

    public RepositoryFieldViewModel(IFolderPicker picker, IRepositories repositories, JobBoard board, NewRepositoryViewModel creation)
    {
        this.picker = picker;
        this.repositories = repositories;
        this.board = board;
        this.creation = creation;
        creation.Created += (_, created) =>
        {
            Repository = created;
            Notice = RepositoryPhrases.Created(created);
        };
    }

    public IReadOnlyList<string> Repositories => known;

    public INewRepositoryViewModel Creation => creation;

    public NewRepositoryViewModel Creating => creation;

    public Task Checking { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial string Repository { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Offer { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    public void ShowKnown() =>
        known.ShowOnly(board.Jobs.Values.OrderByDescending(job => job.Summary.Submitted).Select(job => job.Summary.Repository).Distinct());

    partial void OnRepositoryChanged(string value)
    {
        Notice = string.Empty;
        creation.Near = value;
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
