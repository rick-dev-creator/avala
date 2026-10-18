using System.Collections.ObjectModel;
using Avala.Documentation.Contracts;
using Avala.Workbench.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface IProjectDocsViewModel
{
    bool IsShown { get; }

    int Count { get; }

    bool IsOpen { get; }

    IReadOnlyList<IFeatureRowViewModel> Features { get; }

    bool IsOn { get; }

    bool IsUncommitted { get; }

    string Note { get; }

    string Error { get; }

    IRelayCommand ToggleCommand { get; }

    IAsyncRelayCommand SwitchCommand { get; }

    IAsyncRelayCommand CommitCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ProjectDocsViewModel(ProjectShelf shelf) : IProjectDocsViewModel
{
    private readonly ObservableCollection<FeatureRowViewModel> features = [];
    private string repository = string.Empty;

    public IReadOnlyList<IFeatureRowViewModel> Features => features;

    public IReadOnlyList<FeatureRowViewModel> Listed => features;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial int Count { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchCommand))]
    public partial bool HasRepository { get; private set; }

    [ObservableProperty]
    public partial bool IsOn { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial bool IsUncommitted { get; private set; }

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public async Task ReadAsync(string shown, CancellationToken cancellationToken)
    {
        repository = shown;
        HasRepository = true;
        var docs = await shelf.DocsAsync(shown, cancellationToken);
        var rules = await shelf.DocsRulesAsync(shown, cancellationToken);
        features.Clear();

        foreach (var listed in docs.Features)
        {
            features.Add(new FeatureRowViewModel(listed, () => shelf.OpenFeature(listed.Folder.Match(folder => folder, () => repository), listed.Feature)));
        }

        Count = features.Count;
        IsOn = rules.Working.Enabled;
        IsUncommitted = rules.Working != rules.Committed;
        Note = IsUncommitted ? Settings.DocsPhrases.Uncommitted(IsOn) : string.Empty;
        IsShown = IsOn || IsUncommitted || features.Count > 0;
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand(CanExecute = nameof(HasRepository))]
    private async Task SwitchAsync(CancellationToken cancellationToken)
    {
        var switched = await shelf.SwitchDocsAsync(repository, !IsOn, cancellationToken);
        Error = switched.Match(_ => string.Empty, Settings.DocsPhrases.NotSwitched);
        await ReadAsync(repository, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(IsUncommitted))]
    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        var committed = await shelf.CommitDocsAsync(repository, cancellationToken);
        Error = committed.Match(_ => string.Empty, Settings.DocsPhrases.NotCommitted);
        await ReadAsync(repository, cancellationToken);
    }
}

internal interface IFeatureRowViewModel
{
    string Title { get; }

    string Fact { get; }

    string Tip { get; }

    IRelayCommand OpenCommand { get; }
}

internal sealed partial class FeatureRowViewModel(RepositoryFeature listed, Action open) : IFeatureRowViewModel
{
    public string Title { get; } = listed.Feature.Title;

    public string Fact { get; } = (listed.Versions == 1 ? "1 version" : $"{listed.Versions} versions")
        + listed.Branch.Match(_ => " · not merged", () => string.Empty);

    public string Tip { get; } = listed.Branch.Match(branch => $"On branch {branch}, not merged yet", () => string.Empty);

    [RelayCommand]
    private void Open() => open();
}
