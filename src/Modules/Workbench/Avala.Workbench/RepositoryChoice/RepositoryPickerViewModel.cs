using System.Collections.ObjectModel;
using System.ComponentModel;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal interface IRepositoryChoiceViewModel
{
    string Title { get; }

    string Detail { get; }

    bool IsSelected { get; }

    IRelayCommand ChooseCommand { get; }
}

internal interface IRepositoryPickerViewModel
{
    IRepositoryFieldViewModel Field { get; }

    IReadOnlyList<IRepositoryChoiceViewModel> Recent { get; }

    bool IsScratch { get; }

    string Title { get; }

    string Detail { get; }

    bool IsOpen { get; set; }

    IRelayCommand ChooseScratchCommand { get; }

    IAsyncRelayCommand OpenFolderCommand { get; }

    IAsyncRelayCommand CloneCommand { get; }

    IAsyncRelayCommand NewRepositoryCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RepositoryChoiceViewModel(string repository, Action<string> choose) : IRepositoryChoiceViewModel
{
    public string Repository { get; } = repository;

    public string Title { get; } = RepositoryPhrases.Leaf(repository);

    public string Detail { get; } = Path.GetDirectoryName(repository.TrimEnd('/', '\\')) ?? string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Choose() => choose(Repository);
}

[INotifyPropertyChanged]
internal sealed partial class RepositoryPickerViewModel : IRepositoryPickerViewModel
{
    private readonly ObservableCollection<RepositoryChoiceViewModel> recent = [];
    private readonly RepositoryFieldViewModel chooser;
    private readonly RecentRepositories history;
    private readonly ProjectsPlace projects;
    private bool chosen;

    public RepositoryPickerViewModel(RepositoryFieldViewModel chooser, RecentRepositories history, ProjectsPlace projects)
    {
        this.chooser = chooser;
        this.history = history;
        this.projects = projects;
        chooser.PropertyChanged += OnFieldChanged;
    }

    public IRepositoryFieldViewModel Field => chooser;

    public RepositoryFieldViewModel Chooser => chooser;

    public IReadOnlyList<IRepositoryChoiceViewModel> Recent => recent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Detail))]
    public partial bool IsScratch { get; private set; } = true;

    public string Title => IsScratch ? RepositoryPhrases.Scratch : RepositoryPhrases.Leaf(chooser.Repository);

    public string Detail => IsScratch ? string.Empty : Path.GetDirectoryName(chooser.Repository.Trim().TrimEnd('/', '\\')) ?? string.Empty;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public void ShowKnown()
    {
        chooser.ShowKnown();
        recent.ShowOnly(chooser.Repositories.Select(repository => new RepositoryChoiceViewModel(repository, Choose)));

        if (!chosen)
        {
            history.Last().Match(
                last => last.Scratch ? Scratch() : Use(last.Repository),
                Scratch);
        }

        Mark();
    }

    [RelayCommand]
    private void ChooseScratch()
    {
        chosen = true;
        Scratch();
        IsOpen = false;
    }

    [RelayCommand]
    private async Task OpenFolderAsync(CancellationToken cancellationToken)
    {
        IsOpen = false;
        var picked = await projects.PickAsync(RepositoryPhrases.ChooseTitle, cancellationToken);
        picked.Match<Action>(found => () => Choose(found), () => () => { })();
        await chooser.Checking;
    }

    [RelayCommand]
    private Task CloneAsync(CancellationToken cancellationToken)
    {
        IsOpen = false;

        return chooser.Cloning.StartCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private Task NewRepositoryAsync(CancellationToken cancellationToken)
    {
        IsOpen = false;

        return chooser.Creation.StartCommand.ExecuteAsync(null);
    }

    private void Choose(string repository)
    {
        chosen = true;
        Use(repository);
        IsOpen = false;
    }

    private bool Use(string repository)
    {
        chooser.Repository = repository;
        IsScratch = false;
        Mark();

        return true;
    }

    private bool Scratch()
    {
        IsScratch = true;
        chooser.Repository = string.Empty;
        Mark();

        return true;
    }

    private void Mark()
    {
        foreach (var choice in recent)
        {
            choice.IsSelected = !IsScratch && choice.Repository == chooser.Repository;
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs changed)
    {
        if (changed.PropertyName == nameof(RepositoryFieldViewModel.Repository) && chooser.Repository.Trim().Length > 0)
        {
            chosen = true;
            IsScratch = false;
            Mark();
        }
    }
}
